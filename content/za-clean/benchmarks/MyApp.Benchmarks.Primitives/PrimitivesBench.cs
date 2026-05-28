using BenchmarkDotNet.Attributes;
using MyApp.Api.Dtos;
using MyApp.Api.Mappings;
using MyApp.Application;
using MyApp.Application.CreateOrder;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks.Primitives;

/// <summary>
/// Each framework layer in isolation — no ASP.NET, no Kestrel, no EF.
/// Counterpart to <c>MyApp.Benchmarks.WritePipelineBench</c> (which exercises the full
/// HTTP path). Numbers here directly validate the "zero-allocation through the
/// framework hot path" brand claim. A non-zero <c>Allocated</c> reading on any
/// method (modulo the immutable destination record returned to the caller) is a
/// genuine finding worth investigating.
/// </summary>
[MemoryDiagnoser]
public class PrimitivesBench
{
    private OrderRequest _request = null!;
    private CreateOrderCommand _command;
    private FakeMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _request = new OrderRequest(
            new CustomerId(42),
            new[] { new OrderItemDto("SKU-1", 2, 15m) },
            "1011AA");
        _command = OrderRequestToCommand.Map(_request);
        _mediator = new FakeMediator();
    }

    [Benchmark]
    public CreateOrderCommand Mapping_RequestToCommand()
        => OrderRequestToCommand.Map(_request);

    [Benchmark]
    public async ValueTask<Result<OrderId, ApplicationError>> Mediator_DispatchOnly()
        => await _mediator.Send(_command);

    [Benchmark]
    public UnitResult<ValidationError> Validator_Generated()
        => CreateOrderValidator.Validate(_command);

    [Benchmark]
    public Result<Money, string> ValueObject_TryCreate()
        => Money.TryCreate(10m, "EUR");

    [Benchmark]
    public async ValueTask<Result<OrderId, ApplicationError>> EndToEndPrimitives()
    {
        var cmd = OrderRequestToCommand.Map(_request);
        var validation = CreateOrderValidator.Validate(cmd);
        if (validation.IsFailure)
            return Result<OrderId, ApplicationError>.Failure(new("validation", "failed"));
        return await _mediator.Send(cmd);
    }
}
