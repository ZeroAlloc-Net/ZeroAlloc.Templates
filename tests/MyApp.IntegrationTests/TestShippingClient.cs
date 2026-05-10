using MyApp.Application;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Results;

namespace MyApp.IntegrationTests;

internal sealed class TestShippingClient : IShippingQuoteClient
{
    public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
        => Task.FromResult(Money.TryCreate(5m, "EUR"));
}
