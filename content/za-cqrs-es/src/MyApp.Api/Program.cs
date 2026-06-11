using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MyApp.Api;
using MyApp.Api.Authorization;
using MyApp.Api.Endpoints;
using MyApp.Application;
using MyApp.Domain.ValueObjects;
using MyApp.Infrastructure;
using MyApp.Infrastructure.Projections;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.Authorization.Generated;

var builder = WebApplication.CreateBuilder(args);

// ───────────────────────────────────────────────────────────────────────────
// ZA.ORM (projection repos) + Event Sourcing wiring. Database provider chosen
// via `Database:Provider`; default Sqlite keeps `dotnet run` zero-setup. The
// event store is currently the InMemory adapter (Task 5 swaps in the SQL
// adapter once it ships upstream).
// ───────────────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";

builder.Services.AddMyAppInfrastructure(dbProvider, connectionString);

// ───────────────────────────────────────────────────────────────────────────
// Application composition (IMediator + handlers + projections). The
// HttpSecurityContextAccessor bridges HttpContext.User into ZA.Authorization's
// ISecurityContext so handler-level [RequirePolicy] sees the same identity as
// endpoint-level RequireAuthorization checks.
// ───────────────────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddZeroAllocAuthorization();
builder.Services.AddMyAppApplication(opt => opt.UseAccessor<HttpSecurityContextAccessor>());

// ───────────────────────────────────────────────────────────────────────────
// JWT bearer authentication. DEV signing key MUST be replaced before any
// non-dev deployment — JwtBearer requires at least 32 bytes for HS256.
// ───────────────────────────────────────────────────────────────────────────
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

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("OrdersRead",  p => p.RequireAuthenticatedUser().RequireClaim("scope", "orders.read", "orders.write"))
    .AddPolicy("OrdersWrite", p => p.RequireAuthenticatedUser().RequireClaim("scope", "orders.write"));

// ───────────────────────────────────────────────────────────────────────────
// OpenTelemetry — traces + metrics. Console exporter in dev; OTLP otherwise.
// ───────────────────────────────────────────────────────────────────────────
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
            t.AddConsoleExporter();
        else
            t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        if (consoleTelemetry)
            m.AddConsoleExporter();
        else
            m.AddOtlpExporter();
    });

// AOT: source-generated JSON for DTOs. The [TypedId] converters must be
// registered explicitly — STJ's source generator does not observe
// [JsonConverter] attributes emitted by another source generator (Roslyn
// runs generators in parallel against the original compilation), so
// without these Converters.Add lines OrderId/CustomerId would silently
// serialise as {} via STJ's POCO fallback.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new OrderId.TypedIdJsonConverter());
    o.SerializerOptions.Converters.Add(new CustomerId.TypedIdJsonConverter());
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
});

// Output caching — used by subsequent tasks once GET /orders/{id} lands.
builder.Services.AddOutputCache(opt =>
{
    opt.AddPolicy("OrderById",
        b => b.AddPolicy<CacheAuthenticatedGetsPolicy>().Tag("orders").Expire(TimeSpan.FromSeconds(30)),
        excludeDefaultPolicy: true);
});

var app = builder.Build();

// ───────────────────────────────────────────────────────────────────────────
// Apply ZA.ORM migrations on startup (read-side projection tables). The
// in-memory event store does not require migrations.
// ───────────────────────────────────────────────────────────────────────────
var schemaStrategy = app.Configuration.GetValue<string>("Database:SchemaStrategy") ?? "EmbeddedScript";

if (!string.Equals(schemaStrategy, "Skip", StringComparison.OrdinalIgnoreCase))
{
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
            assembly: typeof(OrderListingsRepository).Assembly,
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
}

app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapOrders();
app.MapGet("/healthz", () => Results.Ok(new HealthResponse("ok"))).AllowAnonymous();

app.Run();

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in integration tests.</summary>
public partial class Program { }

/// <summary>Health-check response. Concrete type so the source-generated JsonContext can serialise it under NativeAOT.</summary>
public sealed record HealthResponse(string Status);
