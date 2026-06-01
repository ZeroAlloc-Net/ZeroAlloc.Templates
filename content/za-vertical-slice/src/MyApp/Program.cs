using System.Data.Async;
using System.Data.Async.Adapters;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using MyApp;
using MyApp.Authorization;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Mediator.Validation;
using ZeroAlloc.ORM.Migrations;
using ZeroAlloc.Serialisation.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Mediator + Validation + Authorization. All handlers live inside this
// assembly under Features/<Area>/<UseCase>/<UseCase>.cs — RegisterHandlersFrom
// Assembly picks them up automatically.
//
// AddZeroAllocAuthorization MUST run before AddMediator().UseAuthorization()
// so the source-generated policy registry is registered before mediator's
// authorization middleware queries it. The D3 guard inside UseAuthorization()
// throws InvalidOperationException if this ordering is violated.
// ---------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();
builder.Services.AddZeroAllocAuthorization();

builder.Services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly)
    .WithValidation()
    .WithAuthorization(o => o.UseAccessor<HttpSecurityContextAccessor>());

// ---------------------------------------------------------------------------
// ZA.ORM substrate — provider selected by `Database:Provider` config
// (Sqlite|Postgres). Default is Sqlite so the zero-setup `dotnet run`
// quickstart still works (`Data Source=app.db`). To target Postgres, set
// `Database:Provider=Postgres` and `ConnectionStrings:Default=Host=...;...`.
//
// Handlers receive IAsyncDbConnection via constructor injection. ZA.ORM's
// source generator emits the SQL execution code for [Query]/[Command] partials
// from that connection — no DbContext, no change-tracker, no reflection.
// Scoped lifetime matches request boundaries.
// ---------------------------------------------------------------------------
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";

builder.Services.AddScoped<IAsyncDbConnection>(_ =>
{
    if (string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        return new NpgsqlConnection(connectionString).AsAsync();
    }
    return new SqliteConnection(connectionString).AsAsync();
});

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
    .AddPolicy("OrdersRead",     p => p.RequireAuthenticatedUser().RequireClaim("scope", "orders.read", "orders.write"))
    .AddPolicy("OrdersWrite",    p => p.RequireAuthenticatedUser().RequireClaim("scope", "orders.write"))
    .AddPolicy("CustomersRead",  p => p.RequireAuthenticatedUser().RequireClaim("scope", "customers.read", "customers.write"))
    .AddPolicy("CustomersWrite", p => p.RequireAuthenticatedUser().RequireClaim("scope", "customers.write"));

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
            .AddSource("ZeroAlloc.Mediator")
            .AddSource("MyApp");
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
        m.AddAspNetCoreInstrumentation()
            .AddMeter("MyApp");
        if (consoleTelemetry)
        {
            m.AddConsoleExporter();
        }
        else
        {
            m.AddOtlpExporter();
        }
    });

// AOT: source-generated JSON metadata for every type that crosses the HTTP
// boundary. With PublishAot=true ASP.NET drops the reflection-based default
// type resolver at runtime; inserting JsonContext.Default at index 0 of the
// resolver chain lets minimal-API endpoints serialise + deserialise without
// reflection. When you add a slice, register its request/response types in
// JsonContext or the host fails to start.
// AddZeroAllocValueObjectConverters registers the typed-ID converters
// emitted by ZeroAlloc.Serialisation 2.3.1's source generator. STJ checks
// options.Converters before the context's typeinfo, so CustomerId/OrderId
// serialize as bare integers instead of wrapped { "Value": 42 } objects.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    o.SerializerOptions.AddZeroAllocValueObjectConverters();
});

#if (EnableSwagger)
builder.Services.AddEndpointsApiExplorer();
#endif

var app = builder.Build();

// Apply schema on startup. `Database:SchemaStrategy` controls how:
//
//   EmbeddedScript  (default) — run ZA.ORM's MigrationRunner over the
//                   Persistence/Migrations/{Sqlite,Postgres}/*.sql resources.
//                   AOT-compatible — no reflection-based ORM.
//   Skip            — startup does nothing. Used by WritePipelineBench's
//                   [GlobalSetup] paths where the bench owns DB lifecycle.
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

    // Build the connection locally here — do NOT resolve IAsyncDbConnection from
    // DI (it's scoped; resolving from the root provider triggers a lifetime
    // captive-dependency exception, and resolving from a scope would tie the
    // migration to the request-scope lifetime).
    System.Data.Common.DbConnection raw = isPostgresSchema
        ? new NpgsqlConnection(connectionString)
        : new SqliteConnection(connectionString);

    await using (raw.ConfigureAwait(false))
    {
        var asyncConn = raw.AsAsync();
        await asyncConn.OpenAsync().ConfigureAwait(false);

        var source = new EmbeddedResourceMigrationSource(
            typeof(Program).Assembly,
            isPostgresSchema
                ? "MyApp.Persistence.Migrations.Postgres."
                : "MyApp.Persistence.Migrations.Sqlite.");
        IMigrationDialect dialect = isPostgresSchema
            ? new PostgresMigrationDialect()
            : new SqliteMigrationDialect();

        var runner = new MigrationRunner(asyncConn, source, dialect);
        var applied = await runner.RunAsync().ConfigureAwait(false);
        app.Logger.LogInformation("Applied {Count} ZA.ORM migrations on startup", applied.Count);

        await asyncConn.CloseAsync().ConfigureAwait(false);
    }
}

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// Endpoint discovery — runtime assembly walk. Every public static class whose
// name ends in "Endpoint" and that exposes a public static
// void Map(IEndpointRouteBuilder) method is invoked once at startup.
//
// This is a vertical-slice convention: each slice owns its own Map call in
// the same file as its request/handler/validator. The walk avoids hand-
// maintaining a central registration list. v0.5 may swap this for a source
// generator if startup time becomes a concern.
// ---------------------------------------------------------------------------
foreach (var endpointType in typeof(Program).Assembly
    .GetTypes()
    .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true } && t.Name.EndsWith("Endpoint", StringComparison.Ordinal))
    .Where(t => t.GetMethod("Map", BindingFlags.Public | BindingFlags.Static, new[] { typeof(IEndpointRouteBuilder) }) is not null))
{
    endpointType
        .GetMethod("Map", BindingFlags.Public | BindingFlags.Static, new[] { typeof(IEndpointRouteBuilder) })!
        .Invoke(null, new object[] { app });
}

// Health check — exposed for liveness/readiness probes.
app.MapHealthChecks("/healthz");

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in integration tests.
/// </summary>
public partial class Program { }
