using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Materials;

/// <summary>A perfumery raw material. Immutable reference data that formulas point to by ID.</summary>
public sealed class Material : Entity<MaterialId>
{
    private Material(
        MaterialId id,
        string name,
        string botanicalSource,
        string origin,
        ExtractionMethod extractionMethod,
        Facets facets,
        double odorStrength,
        Volatility volatility,
        Money costPerKg,
        Dilution stockDilution)
        : base(id)
    {
        Name = name;
        BotanicalSource = botanicalSource;
        Origin = origin;
        ExtractionMethod = extractionMethod;
        Facets = facets;
        OdorStrength = odorStrength;
        Volatility = volatility;
        CostPerKg = costPerKg;
        StockDilution = stockDilution;
    }

    public string Name { get; }

    public string BotanicalSource { get; }

    public string Origin { get; }

    public ExtractionMethod ExtractionMethod { get; }

    public Facets Facets { get; }

    /// <summary>Relative impact multiplier: how loud the material is per unit of mass.</summary>
    public double OdorStrength { get; }

    public Volatility Volatility { get; }

    public NoteTier Tier => Volatility.Tier;

    /// <summary>Cost of one kilogram of the neat material.</summary>
    public Money CostPerKg { get; }

    /// <summary>The dilution the material is kept at in the lab, used when a formula line gives none.</summary>
    public Dilution StockDilution { get; }

    public static Result<Material> Create(
        MaterialId id,
        string name,
        string botanicalSource,
        string origin,
        ExtractionMethod extractionMethod,
        Facets facets,
        double odorStrength,
        Volatility volatility,
        Money costPerKg,
        Dilution stockDilution)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return MaterialErrors.NameRequired;
        }

        if (!Enum.IsDefined(extractionMethod))
        {
            return MaterialErrors.UnknownExtractionMethod;
        }

        if (!double.IsFinite(odorStrength) || odorStrength <= 0d)
        {
            return MaterialErrors.OdorStrengthOutOfRange;
        }

        return new Material(
            id,
            name.Trim(),
            botanicalSource?.Trim() ?? string.Empty,
            origin?.Trim() ?? string.Empty,
            extractionMethod,
            facets,
            odorStrength,
            volatility,
            costPerKg,
            stockDilution);
    }
}
