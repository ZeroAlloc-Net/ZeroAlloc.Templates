using MyApp.Domain.ValueObjects;
using ZeroAlloc.Results;

namespace MyApp.Application;

public interface IShippingQuoteClient
{
    Task<Result<Money, string>> GetQuoteAsync(string zip, CancellationToken ct);
}
