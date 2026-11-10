using System.Text.Json;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;

namespace Perfumery.Infrastructure.Ateliers;

/// <summary>Reads the atelier's tuning parameters from the embedded <c>atelier.era1.json</c>.</summary>
internal static class AtelierSettingsLoader
{
    private const string ResourceName = "Perfumery.Infrastructure.Ateliers.atelier.era1.json";

    public static AtelierSettings Load(decimal? timeScaleOverride = null)
    {
        using var stream = EmbeddedResources.Open(ResourceName);
        return Load(stream, ResourceName, timeScaleOverride);
    }

    internal static AtelierSettings Load(Stream json, string sourceName, decimal? timeScaleOverride = null)
    {
        var dto = JsonSerializer.Deserialize<SettingsDto>(json, EmbeddedResources.JsonOptions)
            ?? throw new InvalidOperationException($"{sourceName} is empty.");

        var equipment = dto.Equipment.ToDictionary(
            pair => pair.Key,
            pair => new EquipmentSpec(
                pair.Value.Count,
                ReferenceData.Require(Mass.FromKilograms(pair.Value.CapacityKg), sourceName, pair.Key.ToString())));

        return ReferenceData.Require(
            AtelierSettings.Create(
                ReferenceData.Require(Money.Create(dto.StartingCapital), sourceName, "startingCapital"),
                equipment,
                dto.FilterLoss,
                timeScaleOverride ?? dto.TimeScale,
                TimeSpan.FromMinutes(dto.ConcentrateMaturationMinutes)),
            sourceName,
            "settings");
    }

    private sealed record SettingsDto(
        decimal StartingCapital,
        decimal FilterLoss,
        decimal TimeScale,
        double ConcentrateMaturationMinutes,
        Dictionary<EquipmentKind, EquipmentDto> Equipment);

    private sealed record EquipmentDto(int Count, decimal CapacityKg);
}
