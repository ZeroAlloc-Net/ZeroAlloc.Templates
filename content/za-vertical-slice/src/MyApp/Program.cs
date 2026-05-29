using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyApp.Persistence;
using MyApp;
using MyApp.Authorization;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Mediator.Validation;
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
// EF Core — provider selected by `Database:Provider` config (Sqlite|Postgres).
// Default is Sqlite so the zero-setup `dotnet run` quickstart still works
// (`Data Source=app.db` flows into UseSqlite). To target Postgres, set
// `Database:Provider=Postgres` and `ConnectionStrings:Default=Host=...;...`.
// ---------------------------------------------------------------------------
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";

// AddDbContextPool reuses DbContext instances across requests — skips the
// constructor + service-resolution cost per call. Material at high RPS
// (NBomber against Postgres saw ~30–50% read-path win in our load tests).
// Requires AppDbContext's constructor to accept only DbContextOptions —
// see AppDbContext.cs.
builder.Services.AddDbContextPool<AppDbContext>(opts =>
{
    if (string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        opts.UseNpgsql(connectionString);
    }
    else
    {
        opts.UseSqlite(connectionString);
    }
    // EF Core 10 fires PendingModelChangesWarning by default when the runtime
    // model snapshot differs from the most recent migration's snapshot — and
    // produces false positives when the compiled-model path (UseModel) is in
    // use alongside committed migrations. Suppressed because regenerating
    // migrations on every CI run isn't a viable workflow for a template.
    opts.ConfigureWarnings(w => w.Ignore(
        Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
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
// OpenTelemetry — traces + metrics with console exporter (dev default). Use
// OTEL_EXPORTER_OTLP_ENDPOINT in deployment to fan traces out to a collector.
// ZA.Telemetry's [Trace]/[Count]/[Histogram] attributes target methods/types
// and feed into the same ActivitySource/Meter pipeline registered here.
// ---------------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("ZeroAlloc.Mediator")
        .AddSource("MyApp")
        .AddConsoleExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddMeter("MyApp")
        .AddConsoleExporter());

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
//   EmbeddedScript  (default) — load schema.sql (Sqlite) or schema.postgres.sql
//                   (Postgres) from embedded resources and apply via raw ADO.NET.
//                   AOT-compatible — no reflection.
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
    // Sqlite branch (sqlite_master → "relation does not exist" against Postgres).
    var schemaProvider = app.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ApplyEmbeddedSchemaAsync(db, schemaProvider);
}

static async Task ApplyEmbeddedSchemaAsync(AppDbContext db, string provider)
{
    var isPostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);
    var resourceSuffix = isPostgres ? "schema.postgres.sql" : "schema.sql";

    var asm = typeof(AppDbContext).Assembly;
    var resourceName = asm.GetManifestResourceNames()
        .First(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
    using var stream = asm.GetManifestResourceStream(resourceName)!;
    using var reader = new StreamReader(stream);
    var script = await reader.ReadToEndAsync();

    var conn = db.Database.GetDbConnection();
    var openedHere = conn.State != System.Data.ConnectionState.Open;
    if (openedHere)
    {
        await conn.OpenAsync();
    }
    try
    {
        await using (var check = conn.CreateCommand())
        {
            check.CommandText = isPostgres
                // `::text` cast forces Postgres to return text instead of the
                // `regclass` type, which Npgsql refuses to map to System.Object
                // for ExecuteScalarAsync. Returns NULL/DBNull if the table is
                // missing, the literal table name if present.
                ? "SELECT to_regclass('public.\"__EFMigrationsHistory\"')::text;"
                : "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            var exists = await check.ExecuteScalarAsync();
            var hasHistory = exists is not null && exists is not DBNull;
            if (hasHistory)
            {
                return;
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = script;
        await cmd.ExecuteNonQueryAsync();
    }
    finally
    {
        if (openedHere)
        {
            await conn.CloseAsync();
        }
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
