using ZeroAlloc.Results;

namespace MyApp.Application.CreateOrder;

/// <summary>
/// First-failure validator for <see cref="CreateOrderCommand"/>.
/// Delegates to the source-generated <see cref="CreateOrderCommandValidator"/>
/// (emitted by ZA.Validation from the <c>[Validate]</c> attribute on the
/// command record) and maps the first failure to the <c>UnitResult&lt;ValidationError&gt;</c>
/// shape <see cref="CreateOrderHandler"/> consumes.
/// </summary>
public static class CreateOrderValidator
{
    private static readonly OrderItemValidator s_itemValidator = new();
    private static readonly CreateOrderCommandValidator s_validator = new(s_itemValidator);

    public static UnitResult<ValidationError> Validate(CreateOrderCommand cmd)
    {
        var result = s_validator.Validate(cmd);
        if (result.IsValid)
            return UnitResult<ValidationError>.Success();

        ref readonly var f = ref result.Failures[0];
        return UnitResult<ValidationError>.Failure(new ValidationError(f.PropertyName, f.ErrorMessage));
    }
}
