using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyApp.Api;
using MyApp.Api.Authorization;
using MyApp.Api.Endpoints;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Infrastructure.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
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
        .AddConsoleExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

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

// Ensure the database schema exists on startup so a fresh dev box doesn't
// need a separate `dotnet ef database update` step. Seed a sample order in
// Development.
//
// NOTE: Under NativeAOT, `Database.MigrateAsync` and `EnsureCreatedAsync`
// fail at runtime — both rely on the design-time EF Core model pipeline,
// which depends on reflection and is not AOT-compatible. Instead, we
// execute an embedded SQL script (`schema.sql`) generated by
// `dotnet ef migrations script`. Regenerate the script after any
// migration change. The script is idempotent for the migrations history
// table; ordinary CREATE TABLE statements are guarded by checking if the
// migrations history table is empty before applying.
// Apply schema on startup. `Database:SchemaStrategy` controls how:
//
//   EmbeddedScript  (default) — load schema.sql (Sqlite) or schema.postgres.sql
//                   (Postgres) from embedded resources and apply via raw ADO.NET.
//                   AOT-compatible — no reflection.
//   Skip            — startup does nothing. Used by WritePipelineBench's
//                   [GlobalSetup] paths where the bench owns DB lifecycle.
var schemaStrategy = builder.Configuration.GetValue<string>("Database:SchemaStrategy")
    ?? "EmbeddedScript";

if (!string.Equals(schemaStrategy, "Skip", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ApplyEmbeddedSchemaAsync(db, dbProvider);
    if (app.Environment.IsDevelopment())
    {
        await SeedData.SeedAsync(db);
    }
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
    // Track whether we opened the connection ourselves; do not close a
    // pre-existing connection (e.g. integration tests share a kept-alive
    // in-memory SQLite connection that backs the entire test session).
    var openedHere = conn.State != System.Data.ConnectionState.Open;
    if (openedHere)
    {
        await conn.OpenAsync();
    }
    try
    {
        // Idempotency check — provider-specific.
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
            // Sqlite returns the table name or null; Postgres returns the table
            // name string (cast from regclass) or DBNull when missing.
            var hasHistory = exists is not null && exists is not DBNull;
            if (hasHistory)
            {
                // Already applied — skip.
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
