using ZeroAlloc.Results;

namespace MyApp.Application.CreateOrder;

/// <summary>
/// First-failure validator for <see cref="CreateOrderCommand"/>.
/// <para>
/// Delegates to the source-generated <see cref="CreateOrderCommandValidator"/>
/// (emitted by ZA.Validation from the <c>[Validate]</c> attribute on the
/// command record). The "at least one item" rule is enforced here because
/// ZA.Validation's <c>[NotEmpty]</c> emits a <c>string.IsNullOrEmpty</c>
/// check and does not yet support <c>IReadOnlyList&lt;T&gt;.Count == 0</c>
/// as a collection-empty rule.
/// </para>
/// </summary>
public static class CreateOrderValidator
{
    private static readonly OrderItemValidator s_itemValidator = new();
    private static readonly CreateOrderCommandValidator s_validator = new(s_itemValidator);

    public static UnitResult<ValidationError> Validate(CreateOrderCommand cmd)
    {
        if (cmd.Items.Count == 0)
            return UnitResult<ValidationError>.Failure(new("Items", "At least one item is required"));

        var result = s_validator.Validate(cmd);
        if (result.IsValid)
            return UnitResult<ValidationError>.Success();

        ref readonly var f = ref result.Failures[0];
        return UnitResult<ValidationError>.Failure(new ValidationError(f.PropertyName, f.ErrorMessage));
    }
}
