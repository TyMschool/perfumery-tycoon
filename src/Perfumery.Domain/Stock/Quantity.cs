using Perfumery.Domain.Common;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Stock;

/// <summary>
/// An amount of a stock item in that item's own unit: kilograms for anything weighed,
/// a count for anything sold by the piece. Held to 5 decimal places (0.01 g).
/// </summary>
public sealed record Quantity
{
    public static readonly Quantity Zero = new(0m);

    private Quantity(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public bool IsZero => Value == 0m;

    public bool IsWhole => Value == decimal.Truncate(Value);

    public static Result<Quantity> Create(decimal value) =>
        value < 0m ? StockErrors.QuantityNegative : new Quantity(Math.Round(value, 5, MidpointRounding.AwayFromZero));

    public static Quantity Of(Mass mass) => new(mass.Kilograms);

    public static Quantity Units(int count) =>
        count < 0 ? throw new ArgumentOutOfRangeException(nameof(count)) : new Quantity(count);

    public static Quantity operator +(Quantity left, Quantity right) => new(left.Value + right.Value);

    public override string ToString() => Value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
}
