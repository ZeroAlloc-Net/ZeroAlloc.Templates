namespace MyApp.Application.CreateOrder;

/// <summary>
/// Stand-in unit type used by the hand-rolled validator's <c>Result&lt;Unit, ValidationError&gt;</c>.
/// </summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
