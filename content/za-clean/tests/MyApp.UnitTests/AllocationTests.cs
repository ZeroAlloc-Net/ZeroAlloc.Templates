using MyApp.Application.CreateOrder;
using MyApp.Domain.ValueObjects;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace MyApp.UnitTests;

/// <summary>
/// Backs the README's "0 B" rows: the source-generated validator and the value object's
/// <c>TryCreate</c> allocate nothing per call. The command is built once, outside the
/// measured action, the same way <c>PrimitivesBench</c> sets it up.
/// </summary>
public class AllocationTests
{
    private const int Iterations = 1_000;

    private static readonly CreateOrderCommand ValidCommand = new(
        CustomerId: new CustomerId(42),
        Items: new[] { new OrderItem("SKU-1", 2, 15m) },
        ShippingZip: "1011AA");

    private static bool _sink;

    [Fact]
    public void Validator_valid_command_allocates_nothing() =>
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            _sink = CreateOrderValidator.Validate(ValidCommand).IsSuccess;
        }, "CreateOrderValidator.Validate");

    [Fact]
    public void Money_TryCreate_success_allocates_nothing() =>
        AllocationGate.AssertBudget(0, Iterations, () =>
        {
            _sink = Money.TryCreate(10m, "EUR").IsSuccess;
        }, "Money.TryCreate");
}
