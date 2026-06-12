using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;
using MyApp.Domain.ValueObjects;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// End-to-end happy path: POST /orders → PlaceOrderHandler → Order aggregate
/// → IEventStore.AppendAsync → IMediator.Publish(OrderPlaced) →
/// OrderListingsProjection.UpsertAsync → order_listings table row exists.
/// </summary>
public sealed class PlaceOrderEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public PlaceOrderEndpointTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task POST_orders_persists_event_and_materializes_projection()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(["orders.write"]));

        var customerId = Guid.NewGuid();
        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId,
            total = 99.99m,
            currency = "EUR",
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var location = resp.Headers.Location?.ToString();
        Assert.NotNull(location);
        // Location header is "/orders/{guid}".
        var idStr = location!.AsSpan("/orders/".Length).ToString();
        var orderId = new OrderId(Guid.Parse(idStr, System.Globalization.CultureInfo.InvariantCulture));

        // Response body shape regression guard. CreatedOrderResponse(OrderId Id)
        // serialises as {"Id":"<guid>"} only when the OrderId [TypedId]
        // converter is registered on the HTTP JsonOptions in Program.cs. If
        // that registration is dropped, STJ source-gen's POCO fallback emits
        // {"Id":{}} silently — pin both the positive shape and the absence
        // of the wrapped {"Value":...} envelope.
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains($"\"{idStr}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Value\":", body, StringComparison.Ordinal);

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderListingsRepository>();
        var listing = await repo.GetByIdAsync(orderId, CancellationToken.None);

        Assert.NotNull(listing);
        Assert.Equal(orderId, listing!.Id);
        Assert.Equal(new CustomerId(customerId), listing.CustomerId);
        Assert.Equal("Placed", listing.Status);
        Assert.Equal(99.99m, listing.Total);
        Assert.Equal("EUR", listing.Currency);
    }

    [Fact]
    public async Task POST_orders_without_jwt_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/orders", new
        {
            customerId = Guid.NewGuid(),
            total = 99.99m,
            currency = "EUR",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
