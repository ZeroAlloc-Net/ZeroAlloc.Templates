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
// EF Core / SQLite.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";

builder.Services.AddDbContext<AppDbContext>(opts =>
{
    opts.UseSqlite(connectionString);
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

// Apply pending migrations on startup so a fresh dev box doesn't need a
// separate `dotnet ef database update` step. Safe to re-run — Migrate()
// is a no-op once the database is up to date.
//
// `Bench:SkipStartupMigrate` is honoured by the WritePipelineBench Postgres
// profile, which substitutes a non-Sqlite DbContext at WebApplicationFactory
// time and creates the schema via EnsureCreated() instead. Production
// configuration never sets this flag.
if (!builder.Configuration.GetValue<bool>("Bench:SkipStartupMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
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
