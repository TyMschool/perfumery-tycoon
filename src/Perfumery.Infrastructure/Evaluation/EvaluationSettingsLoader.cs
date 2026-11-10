using System.Text.Json;
using Perfumery.Domain.Evaluation;

namespace Perfumery.Infrastructure.Evaluation;

/// <summary>Reads the evaluation tuning parameters from the embedded <c>evaluation.json</c>.</summary>
internal static class EvaluationSettingsLoader
{
    private const string ResourceName = "Perfumery.Infrastructure.Evaluation.evaluation.json";

    public static EvaluationSettings Load()
    {
        using var stream = EmbeddedResources.Open(ResourceName);
        var dto = JsonSerializer.Deserialize<SettingsDto>(stream, EmbeddedResources.JsonOptions)
            ?? throw new InvalidOperationException($"{ResourceName} is empty.");

        var settings = EvaluationSettings.Create(
            dto.PerceptionExponent,
            dto.CheckpointMinutes.Select(TimeSpan.FromMinutes),
            dto.TopContributorCount);

        return settings.IsSuccess
            ? settings.Value
            : throw new InvalidOperationException($"{ResourceName} is invalid: {settings.Error.Description}");
    }

    private sealed record SettingsDto(double PerceptionExponent, double[] CheckpointMinutes, int TopContributorCount);
}
