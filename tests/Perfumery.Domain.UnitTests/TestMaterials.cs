using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;

namespace Perfumery.Domain.UnitTests;

/// <summary>Hand-built materials with representative era 1 values, so domain tests need no catalog.</summary>
internal static class TestMaterials
{
    public static readonly Material Bergamot = Build(
        "bergamot-calabria", ExtractionMethod.ColdExpression, strength: 1.0, halfLife: 0.8, cost: 40m, stockPercent: 100m,
        (Facet.Citrus, 0.9), (Facet.Aromatic, 0.3), (Facet.Floral, 0.15), (Facet.Green, 0.1));

    public static readonly Material Lemon = Build(
        "lemon-sicily", ExtractionMethod.ColdExpression, strength: 0.9, halfLife: 0.5, cost: 25m, stockPercent: 100m,
        (Facet.Citrus, 1.0), (Facet.Green, 0.1));

    public static readonly Material Neroli = Build(
        "neroli-vallauris", ExtractionMethod.SteamDistillation, strength: 2.5, halfLife: 2.5, cost: 900m, stockPercent: 10m,
        (Facet.Floral, 0.8), (Facet.Citrus, 0.4), (Facet.Green, 0.3));

    public static readonly Material Petitgrain = Build(
        "petitgrain-bigarade", ExtractionMethod.SteamDistillation, strength: 1.3, halfLife: 1.2, cost: 60m, stockPercent: 100m,
        (Facet.Green, 0.7), (Facet.Citrus, 0.4), (Facet.Aromatic, 0.3), (Facet.Woody, 0.15));

    public static readonly Material Rosemary = Build(
        "rosemary-dalmatia", ExtractionMethod.SteamDistillation, strength: 1.5, halfLife: 1.0, cost: 20m, stockPercent: 100m,
        (Facet.Aromatic, 0.9), (Facet.Green, 0.4), (Facet.Woody, 0.15));

    public static readonly Material RoseOtto = Build(
        "rose-otto-kazanlak", ExtractionMethod.SteamDistillation, strength: 4.0, halfLife: 6.0, cost: 2400m, stockPercent: 10m,
        (Facet.Floral, 1.0), (Facet.Spicy, 0.3));

    public static readonly Material Vetiver = Build(
        "vetiver-reunion", ExtractionMethod.SteamDistillation, strength: 2.0, halfLife: 60.0, cost: 120m, stockPercent: 100m,
        (Facet.Woody, 0.8), (Facet.Earthy, 0.7), (Facet.Green, 0.2));

    public static readonly Material Sandalwood = Build(
        "sandalwood-mysore", ExtractionMethod.SteamDistillation, strength: 0.8, halfLife: 80.0, cost: 150m, stockPercent: 100m,
        (Facet.Woody, 0.9), (Facet.Balsamic, 0.5), (Facet.Powdery, 0.2));

    public static IReadOnlyDictionary<MaterialId, Material> Lookup(params Material[] materials) =>
        materials.ToDictionary(material => material.Id);

    public static Material Numbered(int number) => Build(
        $"material-{number}", ExtractionMethod.SteamDistillation, strength: 1.0, halfLife: 1.0, cost: 10m, stockPercent: 100m,
        (Facet.Green, 0.5));

    public static Parts Parts(decimal value) => Perfumery.Domain.Formulas.Parts.Create(value).Value;

    public static Dilution Percent(decimal value) => Dilution.FromPercent(value).Value;

    private static Material Build(
        string id,
        ExtractionMethod method,
        double strength,
        double halfLife,
        decimal cost,
        decimal stockPercent,
        params (Facet Facet, double Weight)[] facets) =>
        Material.Create(
            MaterialId.Create(id).Value,
            id,
            "Test botanical",
            "Test origin",
            method,
            Facets.Create(facets.Select(f => KeyValuePair.Create(f.Facet, f.Weight))).Value,
            strength,
            Volatility.FromHalfLifeHours(halfLife).Value,
            Money.Create(cost).Value,
            Dilution.FromPercent(stockPercent).Value).Value;
}
