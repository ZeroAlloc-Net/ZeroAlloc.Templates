using MyApp.Infrastructure.External;
using Xunit;
using ZeroAlloc.Resilience;

namespace MyApp.IntegrationTests;

/// <summary>
/// The shipping adapter turns transport failures into a failed result, which CreateOrderHandler
/// reports as "shipping.unavailable", instead of letting them escape as a 500.
/// </summary>
public sealed class ShippingQuoteClientTests
{
    [Fact]
    public async Task Returns_the_quote_as_money()
    {
        var result = await new ShippingQuoteClient(new Http(() => new ShippingQuoteResponse(4.95m, "EUR")))
            .GetQuoteAsync("1011AA", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4.95m, result.Value.Amount);
    }

    [Fact]
    public async Task Reports_exhausted_resilience_policies_as_unavailable()
    {
        var result = await new ShippingQuoteClient(new Http(() => throw new ResilienceException(default, "timed out")))
            .GetQuoteAsync("1011AA", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Shipping quote unavailable: timed out", result.Error);
    }

    [Fact]
    public async Task Reports_http_failures()
    {
        var result = await new ShippingQuoteClient(new Http(() => throw new HttpRequestException("503")))
            .GetQuoteAsync("1011AA", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Shipping quote request failed: 503", result.Error);
    }

    private sealed class Http(Func<ShippingQuoteResponse> respond) : IShippingQuoteHttpClient
    {
        public Task<ShippingQuoteResponse> GetAsync(string zip, CancellationToken ct = default)
            => Task.FromResult(respond());
    }
}
