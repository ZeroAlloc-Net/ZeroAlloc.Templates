using MyApp.Application;
using MyApp.Application.CreateOrder;
using MyApp.Application.GetOrderById;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace MyApp.Benchmarks.Primitives;

/// <summary>
/// Zero-work stub implementing the assembly-local generated <see cref="IMediator"/>
/// partial interface (one <c>Send</c> overload per request handler discovered in
/// MyApp.Application). The bench measures dispatch overhead alone — we deliberately
/// do not invoke the real handler, since that would drag in EF / shipping client etc.
///
/// The success Result is cached in a static field so each Send call is a single
/// boxed ValueTask wrapping a struct — no heap alloc per call.
/// </summary>
internal sealed class FakeMediator : IMediator
{
    private static readonly Result<OrderId, ApplicationError> _createSuccess
        = Result<OrderId, ApplicationError>.Success(new OrderId(1));

    public ValueTask<Result<OrderId, ApplicationError>> Send(
        CreateOrderCommand request, CancellationToken ct = default)
        => new(_createSuccess);

    public ValueTask<Result<OrderReadModel, ApplicationError>> Send(
        GetOrderByIdQuery request, CancellationToken ct = default)
        => throw new NotSupportedException("Primitives bench only exercises CreateOrderCommand dispatch.");
}
