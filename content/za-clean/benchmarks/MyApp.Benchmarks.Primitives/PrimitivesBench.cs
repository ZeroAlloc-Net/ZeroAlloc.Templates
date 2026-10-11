using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyApp.Api.Dtos;
using MyApp.Api.Mappings;
using MyApp.Application;
using MyApp.Application.CreateOrder;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks.Primitives;

/// <summary>
/// Each framework layer in isolation — no ASP.NET, no Kestrel, no database.
/// Counterpart to <c>MyApp.Benchmarks.WritePipelineBench</c> (which exercises the full
/// HTTP path).
/// <para>
/// <see cref="Mediator_Send"/> and <see cref="EndToEndPrimitives"/> dispatch through the
/// real source-generated <see cref="IMediator"/> that <c>AddMyAppApplication</c> registers:
/// the authorization and validation behaviours run, then the real <c>CreateOrderHandler</c>,
/// whose ports are zero-work stubs. Their allocations therefore include the handler's own
/// work (building the <c>Order</c> aggregate and its lines) and the mediator's per-call
/// pipeline cost; <see cref="Handler_Direct"/> measures the handler alone, so the
/// difference is the mediator's share. The mediator is resolved once from a long-lived scope, so per-request
/// scope creation is not measured here; <c>MyApp.Benchmarks.MediatorDispatchBench</c>
/// covers that.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class PrimitivesBench
{
    private OrderRequest _request = null!;
    private CreateOrderCommand _command;
    private ServiceProvider _root = null!;
    private IServiceScope _scope = null!;
    private IMediator _mediator = null!;
    private CreateOrderHandler _handler = null!;

    [GlobalSetup]
    public void Setup()
    {
        _request = new OrderRequest(
            new CustomerId(42),
            new[] { new OrderItemDto("SKU-1", 2, 15m) },
            "1011AA");
        _command = OrderRequestToCommand.Map(_request);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IOrderRepository, StubOrderRepository>();
        services.AddSingleton<IShippingQuoteClient, StubShippingQuoteClient>();
        services.AddScoped<StubSecurityContextAccessor>();
        // Registers the generated [Policy] classes and AuthorizerFor<T> dispatchers;
        // WithAuthorization() inside the call below refuses to run without it.
        services.AddZeroAllocAuthorization();
        services.AddMyAppApplication(o => o.UseAccessor<StubSecurityContextAccessor>());
        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();
        _mediator = _scope.ServiceProvider.GetRequiredService<IMediator>();
        _handler = _scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();

        // Fail fast if the pipeline denies or rejects the command: a bench that
        // measures the failure path would report numbers for the wrong thing.
        var result = _mediator.Send(_command).AsTask().GetAwaiter().GetResult();
        if (!result.IsSuccess)
            throw new InvalidOperationException("Mediator dispatch did not succeed: " + result.Error.Code);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _root.Dispose();
    }

    [Benchmark]
    public CreateOrderCommand Mapping_RequestToCommand()
        => OrderRequestToCommand.Map(_request);

    [Benchmark]
    public async ValueTask<Result<OrderId, ApplicationError>> Mediator_Send()
        => await _mediator.Send(_command).ConfigureAwait(false);

    /// <summary>
    /// The same handler called directly, without the mediator. The difference to
    /// <see cref="Mediator_Send"/> is the mediator's own dispatch and pipeline cost.
    /// </summary>
    [Benchmark]
    public async ValueTask<Result<OrderId, ApplicationError>> Handler_Direct()
        => await _handler.Handle(_command, default).ConfigureAwait(false);

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
        return await _mediator.Send(cmd).ConfigureAwait(false);
    }
}
