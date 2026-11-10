using Perfumery.SharedKernel;

namespace Perfumery.Domain.Materials;

public enum NoteTier
{
    Top,
    Heart,
    Base,
}

/// <summary>How fast a material leaves a blotter, as an evaporation half-life in hours.</summary>
public sealed record Volatility
{
    /// <summary>Half-lives below this are top notes.</summary>
    public const double HeartThresholdHours = 2d;

    /// <summary>Half-lives at or above this are base notes.</summary>
    public const double BaseThresholdHours = 12d;

    private Volatility(double halfLifeHours)
    {
        HalfLifeHours = halfLifeHours;
    }

    public double HalfLifeHours { get; }

    public NoteTier Tier => HalfLifeHours switch
    {
        < HeartThresholdHours => NoteTier.Top,
        < BaseThresholdHours => NoteTier.Heart,
        _ => NoteTier.Base,
    };

    public static Result<Volatility> FromHalfLifeHours(double hours) =>
        double.IsFinite(hours) && hours > 0d ? new Volatility(hours) : VolatilityErrors.HalfLifeOutOfRange;
}

public static class VolatilityErrors
{
    public static readonly Error HalfLifeOutOfRange =
        Error.Validation("Volatility.HalfLifeOutOfRange", "The evaporation half-life must be above 0 hours.");
}
