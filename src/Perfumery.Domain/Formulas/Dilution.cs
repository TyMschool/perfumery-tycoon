using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

/// <summary>The share of neat material in its stock solution. 100% means neat.</summary>
public sealed record Dilution
{
    public static readonly Dilution Neat = new(1m);

    private Dilution(decimal fraction)
    {
        Fraction = fraction;
    }

    /// <summary>Above 0 and up to 1.</summary>
    public decimal Fraction { get; }

    public decimal Percent => Fraction * 100m;

    public bool IsNeat => Fraction == 1m;

    public static Result<Dilution> FromPercent(decimal percent) =>
        percent is <= 0m or > 100m ? DilutionErrors.OutOfRange : new Dilution(percent / 100m);

    public static Result<Dilution> FromFraction(decimal fraction) =>
        fraction is <= 0m or > 1m ? DilutionErrors.OutOfRange : new Dilution(fraction);

    public override string ToString() =>
        IsNeat ? "neat" : Percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
}

public static class DilutionErrors
{
    public static readonly Error OutOfRange =
        Error.Validation("Dilution.OutOfRange", "A dilution must be above 0% and at most 100%.");
}
