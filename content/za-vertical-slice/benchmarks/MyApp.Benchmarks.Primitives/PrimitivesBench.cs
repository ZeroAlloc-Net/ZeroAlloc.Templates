using BenchmarkDotNet.Attributes;
using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks.Primitives;

/// <summary>
/// Each ZeroAlloc primitive in isolation — no ASP.NET, no Kestrel, no EF, no
/// mediator pipeline. Counterpart to <c>MyApp.Benchmarks.WritePipelineBench</c>
/// (which exercises the full HTTP path). Numbers here validate the
/// "zero-allocation through the framework hot path" brand claim. A non-zero
/// <c>Allocated</c> reading on any method is a genuine finding worth
/// investigating.
/// </summary>
[MemoryDiagnoser]
public class PrimitivesBench
{
    private PlaceOrderCommand _command = null!;
    private PlaceOrderCommandValidator _validator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _command = new PlaceOrderCommand(CustomerId: 42, Total: 99.99m);
        _validator = new PlaceOrderCommandValidator();
    }

    /// <summary>TypedId construction — should be a single stack write.</summary>
    [Benchmark]
    public OrderId TypedId_Construct() => new OrderId(1);

    /// <summary>Money.TryCreate — happy path, returns Success.</summary>
    [Benchmark]
    public Result<Money, string> Money_TryCreate_Success() => Money.TryCreate(10m, "EUR");

    /// <summary>Money.TryCreate — sad path, returns Failure(string).</summary>
    [Benchmark]
    public Result<Money, string> Money_TryCreate_Failure() => Money.TryCreate(-1m, "EUR");

    /// <summary>Result&lt;T, TError&gt;.Success construction.</summary>
    [Benchmark]
    public Result<OrderId, Error> Result_Success() =>
        Result<OrderId, Error>.Success(new OrderId(1));

    /// <summary>Result&lt;T, TError&gt;.Failure construction.</summary>
    [Benchmark]
    public Result<OrderId, Error> Result_Failure() =>
        Result<OrderId, Error>.Failure(Error.NotFound("order.not_found", "missing"));

    /// <summary>
    /// Source-generated validator running its full rule set. Two rules
    /// ([GreaterThan(0)] on CustomerId + Total) on the happy path.
    /// </summary>
    [Benchmark]
    public bool Validator_Generated()
    {
        var r = _validator.Validate(_command);
        return r.IsValid;
    }
}
