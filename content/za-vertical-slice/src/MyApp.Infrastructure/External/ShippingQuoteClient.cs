using MyApp.Application;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Inject;
using ZeroAlloc.Resilience;
using ZeroAlloc.Results;

namespace MyApp.Infrastructure.External;

/// <summary>
/// Adapter exposing the application-level <see cref="IShippingQuoteClient"/>
/// contract on top of the ZeroAlloc.Rest-generated <see cref="IShippingQuoteHttpClient"/>.
///
/// Retry + total-timeout resilience is configured on the typed HTTP-client
/// interface itself via <see cref="RetryAttribute"/> and <see cref="TimeoutAttribute"/>;
/// the Resilience source generator wraps the Rest client in a proxy registered
/// behind <see cref="IShippingQuoteHttpClient"/> in DI. This adapter only has
/// to translate exceptions (HTTP failures, resilience exhaustion) into the
/// <see cref="Result{TValue, TError}"/> shape the application layer expects.
/// </summary>
[Scoped]
public sealed class ShippingQuoteClient(IShippingQuoteHttpClient http) : IShippingQuoteClient
{
    public async Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct)
    {
        try
        {
            var response = await http.GetAsync(zip, ct).ConfigureAwait(false);
            return Money.TryCreate(response.Amount, response.Currency);
        }
        catch (ResilienceException ex)
        {
            return Result<Money, string>.Failure($"Shipping quote unavailable: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return Result<Money, string>.Failure($"Shipping quote request failed: {ex.Message}");
        }
    }
}
