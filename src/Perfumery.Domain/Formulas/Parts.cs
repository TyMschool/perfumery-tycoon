using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

/// <summary>
/// A line's amount in a formula, in parts by weight. Parts are relative: shares are
/// always computed from them, so a formula never has to sum to 100.
/// </summary>
public sealed record Parts
{
    public const decimal Min = 0.01m;
    public const decimal Max = 1000m;

    private Parts(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static Result<Parts> Create(decimal value) =>
        value is < Min or > Max ? PartsErrors.OutOfRange : new Parts(value);

    public override string ToString() => Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

public static class PartsErrors
{
    public static readonly Error OutOfRange =
        Error.Validation("Parts.OutOfRange", $"Parts must be between {Parts.Min} and {Parts.Max}.");
}
