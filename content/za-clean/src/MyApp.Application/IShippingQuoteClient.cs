using MyApp.Domain.ValueObjects;
using ZeroAlloc.Results;
using ZeroAlloc.Telemetry;

namespace MyApp.Application;

/// <summary>
/// Application port for the external shipping-quote service.
///
/// <para>
/// <c>[Instrument]</c> makes the ZeroAlloc.Telemetry source generator emit
/// <c>ShippingQuoteClientInstrumented</c>, an internal proxy that wraps any implementation of
/// this interface. Infrastructure registers that proxy as the <see cref="IShippingQuoteClient"/>
/// the handlers receive, so every quote records a <c>shipping.quote</c> span and a
/// <c>shipping.quote.duration_ms</c> histogram sample on the <see cref="Telemetry.SourceName"/>
/// source and meter. The proxy is plain generated code: no reflection, Native AOT safe.
/// </para>
/// </summary>
[Instrument(Telemetry.SourceName)]
public interface IShippingQuoteClient
{
    /// <summary>
    /// Quotes shipping for <paramref name="zip"/>. Failures come back as the
    /// <see cref="Result{TValue, TError}"/> error; the span is marked as an error only when the
    /// call throws.
    /// </summary>
    [Trace("shipping.quote")]
    [Histogram("shipping.quote.duration_ms")]
    Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct);
}
