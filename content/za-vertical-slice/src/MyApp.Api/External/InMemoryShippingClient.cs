using MyApp.Application;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Results;

namespace MyApp.Api.External;

/// <summary>
/// In-memory <see cref="IShippingQuoteClient"/> implementation that bypasses the real
/// ZA.Rest typed HTTP client. Wired in <c>Program.cs</c> only when
/// <c>Shipping:UseStub</c> is true (or <c>MYAPP_Shipping__UseStub=true</c>).
///
/// <para>
/// Returns a constant <c>Money(5, "EUR")</c> regardless of input. Use it for load testing
/// (NBomber needs orders to seed cleanly without hitting a real shipping endpoint) and
/// development against an unreliable network. Never registered in production:
/// <c>Shipping:UseStub</c> defaults to <c>false</c>.
/// </para>
/// </summary>
internal sealed class InMemoryShippingClient : IShippingQuoteClient
{
    public Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
        => Task.FromResult(Money.TryCreate(5m, "EUR"));
}
