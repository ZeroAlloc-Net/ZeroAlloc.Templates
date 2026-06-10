using ZeroAlloc.Results;

#pragma warning disable MA0048 // ValidationError record co-located with the validator

namespace MyApp.Application.Orders.PlaceOrder;

/// <summary>
/// First-failure validator for <see cref="PlaceOrderCommand"/>. Delegates to the
/// source-generated <c>PlaceOrderCommandValidator</c> class emitted by
/// ZA.Validation from the <c>[Validate]</c> attribute on the command record.
/// </summary>
public static class PlaceOrderValidator
{
    private static readonly PlaceOrderCommandValidator s_validator = new();

    public static UnitResult<ValidationError> Validate(PlaceOrderCommand cmd)
    {
        var result = s_validator.Validate(cmd);
        if (result.IsValid)
            return UnitResult<ValidationError>.Success();

        ref readonly var f = ref result.Failures[0];
        return UnitResult<ValidationError>.Failure(new ValidationError(f.PropertyName, f.ErrorMessage));
    }
}

public sealed record ValidationError(string Field, string Message);
