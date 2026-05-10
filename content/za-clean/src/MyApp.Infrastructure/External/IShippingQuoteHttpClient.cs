using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Resilience;

namespace MyApp.Infrastructure.External;

/// <summary>
/// Compile-time generated HTTP client for the shipping quote service.
///
/// Two source generators target this interface at compile time:
/// <list type="bullet">
///   <item><c>ZeroAlloc.Rest.Generator</c> emits the concrete
///   <c>ShippingQuoteHttpClientClient</c> implementation.</item>
///   <item><c>ZeroAlloc.Resilience.Generator</c> emits the
///   <c>IShippingQuoteHttpClientResilienceProxy</c> wrapper which applies the
///   <see cref="RetryAttribute"/> + <see cref="TimeoutAttribute"/> policy below.</item>
/// </list>
/// Wire them together at startup with
/// <c>services.AddRestResilience&lt;IShippingQuoteHttpClient, ShippingQuoteHttpClientClient, IShippingQuoteHttpClientResilienceProxy&gt;(...)</c>.
/// </summary>
[ZeroAllocRestClient]
[Retry(MaxAttempts = 3, BackoffMs = 200, Jitter = true)]
[Timeout(Ms = 5_000)]
public interface IShippingQuoteHttpClient
{
    [Get("/quote/{zip}")]
    Task<ShippingQuoteResponse> GetAsync(string zip, CancellationToken ct = default);
}
