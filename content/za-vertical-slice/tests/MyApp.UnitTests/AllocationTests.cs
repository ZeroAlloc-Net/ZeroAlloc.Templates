using MyApp.Common;
using MyApp.Features.Orders.PlaceOrder;
using Xunit;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace MyApp.UnitTests;

/// <summary>
/// Backs the README's "0 B" rows: the typed id, the value object's <c>TryCreate</c>, the
/// success <c>Result</c> and the source-generated validator allocate nothing per call. Inputs
/// are built once, outside the measured action, the same way <c>PrimitivesBench</c> sets them up.
/// </summary>
public class AllocationTests
{
    private const int Iterations = 1_000;

    private static readonly PlaceOrderCommand ValidCommand = new(CustomerId: new CustomerId(42), Total: 99.99m);
    private static readonly PlaceOrderCommandValidator Validator = new();

    private static bool _sink;

    [Fact]
    public void TypedId_construct_allocates_nothing() =>
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            _sink = new OrderId(1).Value == 1;
        }, "new OrderId");

    [Fact]
    public void Money_TryCreate_success_allocates_nothing() =>
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            _sink = Money.TryCreate(10m, "EUR").IsSuccess;
        }, "Money.TryCreate");

    [Fact]
    public void Result_success_allocates_nothing() =>
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            _sink = Result<OrderId, Error>.Success(new OrderId(1)).IsSuccess;
        }, "Result.Success");

    [Fact]
    public void Validator_valid_command_allocates_nothing() =>
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            _sink = Validator.Validate(ValidCommand).IsValid;
        }, "PlaceOrderCommandValidator.Validate");
}
