using Perfumery.SharedKernel;

namespace Perfumery.Domain.Common;

/// <summary>An amount by weight, held in grams to the nearest 0.01 g.</summary>
public sealed record Mass : IComparable<Mass>
{
    public static readonly Mass Zero = new(0m);

    private Mass(decimal grams)
    {
        Grams = grams;
    }

    public decimal Grams { get; }

    public decimal Kilograms => Grams / 1000m;

    public bool IsZero => Grams == 0m;

    public static Result<Mass> FromGrams(decimal grams) =>
        grams < 0m ? MassErrors.Negative : new Mass(Math.Round(grams, 2, MidpointRounding.AwayFromZero));

    public static Result<Mass> FromKilograms(decimal kilograms) => FromGrams(kilograms * 1000m);

    public static Mass operator +(Mass left, Mass right) => new(left.Grams + right.Grams);

    /// <summary>Subtracts, failing a bug check if the result would be negative.</summary>
    public static Mass operator -(Mass left, Mass right) =>
        right.Grams > left.Grams
            ? throw new InvalidOperationException("A mass cannot go below zero.")
            : new Mass(left.Grams - right.Grams);

    public static bool operator <(Mass left, Mass right) => left.Grams < right.Grams;

    public static bool operator >(Mass left, Mass right) => left.Grams > right.Grams;

    public static bool operator <=(Mass left, Mass right) => left.Grams <= right.Grams;

    public static bool operator >=(Mass left, Mass right) => left.Grams >= right.Grams;

    /// <summary>Scales by a non-negative factor, rounding to 0.01 g.</summary>
    public Mass Times(decimal factor) =>
        factor < 0m
            ? throw new ArgumentOutOfRangeException(nameof(factor), "A mass cannot be scaled by a negative factor.")
            : new Mass(Math.Round(Grams * factor, 2, MidpointRounding.AwayFromZero));

    public int CompareTo(Mass? other) => other is null ? 1 : Grams.CompareTo(other.Grams);

    public override string ToString() =>
        Grams >= 1000m
            ? Kilograms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " kg"
            : Grams.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " g";
}

public static class MassErrors
{
    public static readonly Error Negative = Error.Validation("Mass.Negative", "A mass cannot be negative.");

    public static readonly Error ZeroNotAllowed = Error.Validation("Mass.Zero", "The mass must be above zero.");
}
