using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyApp.Api;
using MyApp.Api.Endpoints;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Infrastructure.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// EF Core + Infrastructure composition (DbContext, [Scoped] services, typed HTTP
// client wrapped through the ZA.Rest + ZA.Resilience proxy).
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=app.db";
var shippingBaseUrl = builder.Configuration["Shipping:BaseUrl"]
    ?? "https://shipping.example/";

builder.Services.AddMyAppInfrastructure(connectionString, shippingBaseUrl);

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
// ---------------------------------------------------------------------------
builder.Services.AddMyAppApplication();

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

var app = builder.Build();

// Apply migrations on startup so a fresh dev box doesn't need a separate
// `dotnet ef database update` step. Seed a sample order in Development.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
    {
        await SeedData.SeedAsync(db);
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapOrders();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in integration tests.
/// </summary>
public partial class Program { }
