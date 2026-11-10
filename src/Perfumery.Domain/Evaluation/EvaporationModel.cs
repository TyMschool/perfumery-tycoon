using Perfumery.Domain.Materials;

namespace Perfumery.Domain.Evaluation;

/// <summary>The maths of a material fading from a blotter and of how loud it smells.</summary>
public static class EvaporationModel
{
    /// <summary>Share of a material still on the blotter after <paramref name="elapsed"/>: r(t) = 2^(−t / h).</summary>
    public static double RemainingShare(Volatility volatility, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(volatility);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        return Math.Pow(2d, -elapsed.TotalHours / volatility.HalfLifeHours);
    }

    /// <summary>Raw intensity: I = neat fraction × odor strength × remaining share.</summary>
    public static double RawIntensity(double neatFraction, double odorStrength, double remainingShare) =>
        neatFraction * odorStrength * remainingShare;

    /// <summary>Perceived intensity grows slower than concentration: P = I^k.</summary>
    public static double PerceivedIntensity(double rawIntensity, double exponent) =>
        rawIntensity <= 0d ? 0d : Math.Pow(rawIntensity, exponent);
}
