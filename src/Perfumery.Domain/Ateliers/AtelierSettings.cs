using Perfumery.Domain.Common;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Ateliers;

public enum EquipmentKind
{
    Still,
    TinctureJar,
    MacerationVessel,
}

/// <param name="Count">How many the atelier has.</param>
/// <param name="Capacity">The most one of them holds in a single charge.</param>
public sealed record EquipmentSpec(int Count, Mass Capacity);

/// <summary>The atelier's tuning parameters for an era. Supplied from configuration.</summary>
public sealed class AtelierSettings
{
    private AtelierSettings(
        Money startingCapital,
        IReadOnlyDictionary<EquipmentKind, EquipmentSpec> equipment,
        decimal filterLoss,
        decimal timeScale,
        TimeSpan concentrateMaturation)
    {
        StartingCapital = startingCapital;
        Equipment = equipment;
        FilterLoss = filterLoss;
        TimeScale = timeScale;
        ConcentrateMaturation = concentrateMaturation;
    }

    public Money StartingCapital { get; }

    public IReadOnlyDictionary<EquipmentKind, EquipmentSpec> Equipment { get; }

    /// <summary>Share of a product run lost at filtering, 0 up to but not including 1.</summary>
    public decimal FilterLoss { get; }

    /// <summary>Durations are divided by this when a job starts. 1 is normal play.</summary>
    public decimal TimeScale { get; }

    public TimeSpan ConcentrateMaturation { get; }

    public static Result<AtelierSettings> Create(
        Money startingCapital,
        IReadOnlyDictionary<EquipmentKind, EquipmentSpec> equipment,
        decimal filterLoss,
        decimal timeScale,
        TimeSpan concentrateMaturation)
    {
        ArgumentNullException.ThrowIfNull(startingCapital);
        ArgumentNullException.ThrowIfNull(equipment);

        if (Enum.GetValues<EquipmentKind>().Any(kind =>
                !equipment.TryGetValue(kind, out var spec) || spec.Count < 0 || spec.Capacity.IsZero))
        {
            return AtelierErrors.InvalidEquipment;
        }

        if (filterLoss is < 0m or >= 1m)
        {
            return AtelierErrors.InvalidFilterLoss;
        }

        if (timeScale <= 0m)
        {
            return AtelierErrors.InvalidTimeScale;
        }

        if (concentrateMaturation < TimeSpan.Zero)
        {
            return AtelierErrors.InvalidDuration;
        }

        return new AtelierSettings(
            startingCapital,
            new Dictionary<EquipmentKind, EquipmentSpec>(equipment),
            filterLoss,
            timeScale,
            concentrateMaturation);
    }

    /// <summary>A reference duration as it runs in this game: divided by the time scale.</summary>
    public TimeSpan Scale(TimeSpan duration) => TimeSpan.FromTicks((long)(duration.Ticks / TimeScale));

    /// <summary>Fails when every unit of the equipment is occupied.</summary>
    public Result CheckFree(EquipmentKind kind, int inUse) =>
        inUse >= Equipment[kind].Count ? AtelierErrors.NoFreeEquipment(kind) : Result.Success();
}
