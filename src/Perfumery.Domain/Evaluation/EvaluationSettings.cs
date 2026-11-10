using Perfumery.SharedKernel;

namespace Perfumery.Domain.Evaluation;

/// <summary>Tuning parameters of the evaluation model. Supplied from configuration, not hard-coded at call sites.</summary>
public sealed record EvaluationSettings
{
    public static readonly EvaluationSettings Default = new(
        0.5,
        [
            TimeSpan.Zero,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(24),
        ],
        3);

    private EvaluationSettings(double perceptionExponent, IReadOnlyList<TimeSpan> checkpoints, int topContributorCount)
    {
        PerceptionExponent = perceptionExponent;
        Checkpoints = checkpoints;
        TopContributorCount = topContributorCount;
    }

    /// <summary>The power-law exponent k: perceived intensity = raw intensity ^ k.</summary>
    public double PerceptionExponent { get; }

    /// <summary>Times since application to the blotter, in ascending order.</summary>
    public IReadOnlyList<TimeSpan> Checkpoints { get; }

    public int TopContributorCount { get; }

    public static Result<EvaluationSettings> Create(
        double perceptionExponent,
        IEnumerable<TimeSpan> checkpoints,
        int topContributorCount = 3)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);

        if (!double.IsFinite(perceptionExponent) || perceptionExponent <= 0d || perceptionExponent > 1d)
        {
            return EvaluationErrors.ExponentOutOfRange;
        }

        var times = checkpoints.ToArray();
        if (times.Length == 0 || times[0] < TimeSpan.Zero || times.Zip(times.Skip(1)).Any(pair => pair.Second <= pair.First))
        {
            return EvaluationErrors.InvalidCheckpoints;
        }

        if (topContributorCount < 1)
        {
            return EvaluationErrors.InvalidContributorCount;
        }

        return new EvaluationSettings(perceptionExponent, times, topContributorCount);
    }
}

public static class EvaluationErrors
{
    public static readonly Error ExponentOutOfRange =
        Error.Validation("Evaluation.ExponentOutOfRange", "The perception exponent must be above 0 and at most 1.");

    public static readonly Error InvalidCheckpoints =
        Error.Validation("Evaluation.InvalidCheckpoints", "Checkpoints must be non-negative, distinct and in ascending order.");

    public static readonly Error InvalidContributorCount =
        Error.Validation("Evaluation.InvalidContributorCount", "At least one top contributor must be reported.");
}
