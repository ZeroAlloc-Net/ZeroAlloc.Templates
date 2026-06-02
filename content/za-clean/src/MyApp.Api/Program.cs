using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MyApp.Api;
using MyApp.Api.Authorization;
using MyApp.Api.Endpoints;
using MyApp.Application;
using MyApp.Infrastructure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Serialisation.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// EF Core + Infrastructure composition (DbContext, [Scoped] services, typed HTTP
// client wrapped through the ZA.Rest + ZA.Resilience proxy).
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";
var shippingBaseUrl = builder.Configuration["Shipping:BaseUrl"]
    ?? "https://shipping.example/";
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";

builder.Services.AddMyAppInfrastructure(dbProvider, connectionString, shippingBaseUrl);

// Load-test / dev convenience: swap the ZA.Rest typed shipping client for an
// in-memory stub when Shipping:UseStub=true. Override via the
// Shipping__UseStub=true env var for one-shot runs (e.g. NBomber).
// Defaults to false — production deployments are untouched.
if (builder.Configuration.GetValue<bool>("Shipping:UseStub"))
{
    var descriptor = builder.Services.Single(d => d.ServiceType == typeof(MyApp.Application.IShippingQuoteClient));
    builder.Services.Remove(descriptor);
    builder.Services.AddScoped<MyApp.Application.IShippingQuoteClient, MyApp.Api.External.InMemoryShippingClient>();
}

// ---------------------------------------------------------------------------
// Application composition (IMediator dispatcher + handler registrations).
// AddMyAppApplication takes an AuthorizationOptions callback that ZA.Mediator
// .Authorization uses to resolve the per-request ISecurityContext. The
// HttpSecurityContextAccessor below bridges HttpContext.User (ClaimsPrincipal
// populated by JwtBearer) into ZA.Authorization's ISecurityContext shape,
// so handler-level [RequirePolicy] policies see the same identity as endpoint-
// level RequireAuthorization checks.
// ---------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
// v2: register the source-generated policy registry (AuthorizerFor<T> dispatchers +
// [Policy] classes as scoped) before WithAuthorization() runs. The D3 guard inside
// AddMyAppApplication's WithAuthorization() throws InvalidOperationException if this
// call is missing or runs after AddMediator(). See ZeroAlloc.Authorization v2 docs.
builder.Services.AddZeroAllocAuthorization();
builder.Services.AddMyAppApplication(opt => opt.UseAccessor<HttpSecurityContextAccessor>());

// ---------------------------------------------------------------------------
// JWT bearer authentication. The DEV signing key MUST be replaced before any
// non-dev deployment — JwtBearer requires at least 32 bytes for HS256.
// ---------------------------------------------------------------------------
var jwtSigningKey = builder.Configuration["Jwt:DevSigningKey"]
    ?? throw new InvalidOperationException(
        "Configure 'Jwt:DevSigningKey' (>=32 chars). NEVER ship the default in production.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
        };
    });

// RequireClaim("scope", "orders.read", "orders.write") accepts a token that has
// EITHER value — so a writer can also read. OrdersWrite is strict: only the
// "orders.write" scope passes. A token without the scope claim returns 403.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("OrdersRead",  p => p.RequireAuthenticatedUser().RequireClaim("scope", "orders.read", "orders.write"))
    .AddPolicy("OrdersWrite", p => p.RequireAuthenticatedUser().RequireClaim("scope", "orders.write"));

// ---------------------------------------------------------------------------
// OpenTelemetry — traces + metrics.
//
// Exporter selection is environment-gated, and it matters for throughput. The
// console exporter is registered with a SimpleActivityExportProcessor: every
// span is written to stdout SYNCHRONOUSLY, inline on the request thread, under
// the global Console.Out lock. Great for eyeballing traces in dev; under load
// it serialises every request on that lock and dominates tail latency. So:
//
//   Development → console exporter (immediate, human-readable, low volume).
//   otherwise   → OTLP exporter, which uses a BatchActivityExportProcessor:
//                 spans are queued and drained on a background thread, off the
//                 request hot path. Honours OTEL_EXPORTER_OTLP_ENDPOINT
//                 (defaults to localhost:4317).
//
// Trace sampling is config-gated. `Telemetry:TraceSampleRatio` (default 1.0 =
// sample everything) feeds a parent-based ratio sampler. Under sustained high
// RPS, dial this down (e.g. 0.05) so you stop minting a span per request
// regardless of which exporter is attached.
//
// ZA.Telemetry's [Trace]/[Count]/[Histogram] attributes target methods/types
// and feed into the same ActivitySource/Meter pipeline registered here.
// ---------------------------------------------------------------------------
var traceSampleRatio = builder.Configuration.GetValue<double?>("Telemetry:TraceSampleRatio") ?? 1.0;
var consoleTelemetry = builder.Environment.IsDevelopment();

builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(traceSampleRatio)))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("ZeroAlloc.Mediator");
        if (consoleTelemetry)
        {
            t.AddConsoleExporter();
        }
        else
        {
            t.AddOtlpExporter();
        }
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        if (consoleTelemetry)
        {
            m.AddConsoleExporter();
        }
        else
        {
            m.AddOtlpExporter();
        }
    });

// AOT: source-generated JSON for DTOs. Insert JsonContext.Default at index 0
// so the generated resolver wins over the reflection-based default.
// AddZeroAllocValueObjectConverters registers the typed-ID converters emitted
// by ZeroAlloc.Serialisation 2.3.1's source generator — STJ consults
// options.Converters before the context's typeinfo, so CustomerId/OrderId
// serialize as bare integers instead of wrapped { "Value": 42 } objects.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    o.SerializerOptions.AddZeroAllocValueObjectConverters();
});

var app = builder.Build();

// Apply schema on startup. `Database:SchemaStrategy` controls how:
//   EmbeddedScript  (default) — run ZA.ORM MigrationRunner over the
//                                Persistence/Migrations/{Sqlite,Postgres}/*.sql resources.
//   Skip            — assume an external pipeline applied the schema (CI,
//                     production migration tooling, container init-script).
var schemaStrategy = app.Configuration.GetValue<string>("Database:SchemaStrategy")
    ?? "EmbeddedScript";

if (!string.Equals(schemaStrategy, "Skip", StringComparison.OrdinalIgnoreCase))
{
    // Re-read `Database:Provider` from app.Configuration here (NOT the
    // `dbProvider` local captured before builder.Build()). Under WebApplicationFactory,
    // ConfigureAppConfiguration overrides take effect during builder.Build();
    // the top-level capture happens BEFORE Build and would see the default
    // ("Sqlite"), routing the Postgres bench's idempotency check through the
    // Sqlite branch.
    var schemaProvider = app.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
    var isPostgresSchema = string.Equals(schemaProvider, "Postgres", StringComparison.OrdinalIgnoreCase);

    System.Data.Common.DbConnection raw = isPostgresSchema
        ? new Npgsql.NpgsqlConnection(connectionString)
        : new Microsoft.Data.Sqlite.SqliteConnection(connectionString);

    await using (raw.ConfigureAwait(false))
    {
        var asyncConn = raw.AsAsync();
        await asyncConn.OpenAsync().ConfigureAwait(false);

        var source = new ZeroAlloc.ORM.Migrations.EmbeddedResourceMigrationSource(
            assembly: typeof(MyApp.Infrastructure.Persistence.OrderRepository).Assembly,
            resourceNamespacePrefix: isPostgresSchema
                ? "MyApp.Infrastructure.Persistence.Migrations.Postgres."
                : "MyApp.Infrastructure.Persistence.Migrations.Sqlite.");

        ZeroAlloc.ORM.Migrations.IMigrationDialect dialect = isPostgresSchema
            ? new ZeroAlloc.ORM.Migrations.PostgresMigrationDialect()
            : new ZeroAlloc.ORM.Migrations.SqliteMigrationDialect();

        var runner = new ZeroAlloc.ORM.Migrations.MigrationRunner(asyncConn, source, dialect);
        var applied = await runner.RunAsync().ConfigureAwait(false);
        app.Logger.LogInformation("Applied {Count} ZA.ORM migrations on startup", applied.Count);

        await asyncConn.CloseAsync().ConfigureAwait(false);
    }

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        await SeedData.SeedAsync(repo);
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapOrders();
// Use a concrete record type rather than an anonymous object so the
// source-generated JsonContext can serialise the response under AOT.
app.MapGet("/healthz", () => Results.Ok(new HealthResponse("ok"))).AllowAnonymous();

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in integration tests.
/// </summary>
public partial class Program { }

/// <summary>
/// Health-check response. Concrete (non-anonymous) so the source-generated
/// <see cref="MyApp.Api.JsonContext"/> can serialise it under NativeAOT.
/// </summary>
public sealed record HealthResponse(string Status);
