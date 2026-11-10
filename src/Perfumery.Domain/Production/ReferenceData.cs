using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Production;

/// <summary>Share of concentrate in a finished product, by mass. Above 0 and below 100%.</summary>
public sealed record Concentration
{
    private Concentration(decimal fraction)
    {
        Fraction = fraction;
    }

    public decimal Fraction { get; }

    public decimal Percent => Fraction * 100m;

    public static Result<Concentration> FromFraction(decimal fraction) =>
        fraction is <= 0m or >= 1m ? ProductionErrors.ConcentrationInvalid : new Concentration(fraction);

    public static Result<Concentration> FromPercent(decimal percent) => FromFraction(percent / 100m);

    public override string ToString() => Percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
}

/// <summary>
/// Output mass per kg of raw botanical. For a tincture the output includes the spirit,
/// less what the spent material retains.
/// </summary>
public sealed record Yield
{
    private Yield(decimal kgPerKgRaw)
    {
        KgPerKgRaw = kgPerKgRaw;
    }

    public decimal KgPerKgRaw { get; }

    public static Result<Yield> Create(decimal kgPerKgRaw) =>
        kgPerKgRaw <= 0m ? ExtractionErrors.YieldOutOfRange : new Yield(kgPerKgRaw);
}

/// <summary>When a timed job started and when it completes. Completion is fixed at the start.</summary>
public sealed record JobTiming
{
    private JobTiming(DateTimeOffset startedAt, DateTimeOffset completesAt)
    {
        StartedAt = startedAt;
        CompletesAt = completesAt;
    }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletesAt { get; }

    public static Result<JobTiming> Create(DateTimeOffset startedAt, DateTimeOffset completesAt) =>
        completesAt < startedAt ? ProductionErrors.TimingInvalid : new JobTiming(startedAt, completesAt);

    public static JobTiming Starting(DateTimeOffset now, TimeSpan duration) =>
        duration < TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(duration))
            : new JobTiming(now, now + duration);

    public bool IsComplete(DateTimeOffset now) => now >= CompletesAt;

    public TimeSpan Remaining(DateTimeOffset now) => IsComplete(now) ? TimeSpan.Zero : CompletesAt - now;
}

public sealed record ExtractionRecipeId
{
    private ExtractionRecipeId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ExtractionRecipeId> Create(string? value) =>
        Slug.IsValid(value) ? new ExtractionRecipeId(value!) : ExtractionErrors.RecipeIdMalformed;

    public override string ToString() => Value;
}

/// <param name="Raw">Raw botanical charged.</param>
/// <param name="Spirit">Spirit charged with it (tinctures only).</param>
/// <param name="Output">Material the run will yield.</param>
public sealed record ExtractionCharge(Mass Raw, Mass Spirit, Mass Output)
{
    /// <summary>Everything that goes into the equipment.</summary>
    public Mass Total => Raw + Spirit;
}

/// <summary>How the atelier makes one material from a raw botanical.</summary>
public sealed class ExtractionRecipe : Entity<ExtractionRecipeId>
{
    private ExtractionRecipe(
        ExtractionRecipeId id,
        MaterialId output,
        ExtractionMethod method,
        SupplyId rawBotanical,
        decimal spiritPerKgRaw,
        Yield yield,
        TimeSpan duration,
        EquipmentKind equipment)
        : base(id)
    {
        Output = output;
        Method = method;
        RawBotanical = rawBotanical;
        SpiritPerKgRaw = spiritPerKgRaw;
        Yield = yield;
        Duration = duration;
        Equipment = equipment;
    }

    public MaterialId Output { get; }

    public ExtractionMethod Method { get; }

    public SupplyId RawBotanical { get; }

    /// <summary>Kilograms of spirit per kilogram of raw botanical. Zero for distillation.</summary>
    public decimal SpiritPerKgRaw { get; }

    public Yield Yield { get; }

    public TimeSpan Duration { get; }

    public EquipmentKind Equipment { get; }

    public static Result<ExtractionRecipe> Create(
        ExtractionRecipeId id,
        MaterialId output,
        ExtractionMethod method,
        SupplyId rawBotanical,
        decimal spiritPerKgRaw,
        Yield yield,
        TimeSpan duration,
        EquipmentKind equipment)
    {
        if (spiritPerKgRaw < 0m || duration <= TimeSpan.Zero || !Enum.IsDefined(method) || !Enum.IsDefined(equipment))
        {
            return ExtractionErrors.RecipeInvalid;
        }

        return new ExtractionRecipe(id, output, method, rawBotanical, spiritPerKgRaw, yield, duration, equipment);
    }

    public ExtractionCharge ChargeFor(Mass rawMass) =>
        new(rawMass, rawMass.Times(SpiritPerKgRaw), rawMass.Times(Yield.KgPerKgRaw));
}

public sealed record ProductTypeId
{
    private ProductTypeId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ProductTypeId> Create(string? value) =>
        Slug.IsValid(value) ? new ProductTypeId(value!) : ProductionErrors.ProductTypeIdMalformed;

    public override string ToString() => Value;
}

/// <summary>A kind of finished product an era's house sells, e.g. an eau de Cologne.</summary>
public sealed class ProductType : Entity<ProductTypeId>
{
    private ProductType(
        ProductTypeId id,
        string name,
        Concentration minConcentration,
        Concentration maxConcentration,
        Concentration defaultConcentration,
        TimeSpan maceration,
        SupplyId flacon,
        Mass flaconFill)
        : base(id)
    {
        Name = name;
        MinConcentration = minConcentration;
        MaxConcentration = maxConcentration;
        DefaultConcentration = defaultConcentration;
        Maceration = maceration;
        Flacon = flacon;
        FlaconFill = flaconFill;
    }

    public string Name { get; }

    public Concentration MinConcentration { get; }

    public Concentration MaxConcentration { get; }

    public Concentration DefaultConcentration { get; }

    public TimeSpan Maceration { get; }

    public SupplyId Flacon { get; }

    /// <summary>Mass of product one flacon holds.</summary>
    public Mass FlaconFill { get; }

    public static Result<ProductType> Create(
        ProductTypeId id,
        string name,
        Concentration minConcentration,
        Concentration maxConcentration,
        Concentration defaultConcentration,
        TimeSpan maceration,
        SupplyId flacon,
        Mass flaconFill)
    {
        if (string.IsNullOrWhiteSpace(name)
            || minConcentration.Fraction > maxConcentration.Fraction
            || defaultConcentration.Fraction < minConcentration.Fraction
            || defaultConcentration.Fraction > maxConcentration.Fraction
            || maceration < TimeSpan.Zero
            || flaconFill.IsZero)
        {
            return ProductionErrors.ProductTypeInvalid;
        }

        return new ProductType(
            id, name.Trim(), minConcentration, maxConcentration, defaultConcentration, maceration, flacon, flaconFill);
    }

    public bool Allows(Concentration concentration) =>
        concentration.Fraction >= MinConcentration.Fraction && concentration.Fraction <= MaxConcentration.Fraction;
}
