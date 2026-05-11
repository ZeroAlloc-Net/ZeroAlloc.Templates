using System.Text.RegularExpressions;
using ZeroAlloc.Results;

namespace MyApp.Application.CreateOrder;

/// <summary>
/// Hand-rolled validator for <see cref="CreateOrderCommand"/>.
/// <para>
/// Replaces the ZA.Validation source-generated validator while the published
/// nupkg ships without its analyzer. Migrate to attribute-based validation
/// when upstream fix lands.
/// </para>
/// </summary>
public static partial class CreateOrderValidator
{
    // Anchored, fixed-length, no backtracking — safe from ReDoS.
#pragma warning disable MA0009 // ReDoS — false positive: pattern is anchored and fixed-length.
    [GeneratedRegex("^[0-9]{4}[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ZipPattern();
#pragma warning restore MA0009

    public static UnitResult<ValidationError> Validate(CreateOrderCommand cmd)
    {
        if (cmd.CustomerId <= 0)
            return UnitResult<ValidationError>.Failure(new("CustomerId", "CustomerId must be positive"));

        if (cmd.Items.Count == 0)
            return UnitResult<ValidationError>.Failure(new("Items", "At least one item is required"));

        for (var i = 0; i < cmd.Items.Count; i++)
        {
            var item = cmd.Items[i];
            if (item.Quantity <= 0)
                return UnitResult<ValidationError>.Failure(new($"Items[{i}].Quantity", "Quantity must be positive"));
            if (item.UnitPriceEur < 0)
                return UnitResult<ValidationError>.Failure(new($"Items[{i}].UnitPriceEur", "UnitPrice must be non-negative"));
        }

        if (string.IsNullOrEmpty(cmd.ShippingZip) || !ZipPattern().IsMatch(cmd.ShippingZip))
            return UnitResult<ValidationError>.Failure(new("ShippingZip", "ShippingZip must match ^[0-9]{4}[A-Z]{2}$"));

        return UnitResult<ValidationError>.Success();
    }
}
