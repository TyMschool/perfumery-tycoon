using Perfumery.Application.Abstractions.Materials;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;

namespace Perfumery.Application.UnitTests.Fakes;

/// <summary>A three-material catalog: one top, one heart, one base note.</summary>
internal sealed class FakeMaterialCatalog : IMaterialCatalog
{
    public const string BergamotId = "bergamot-calabria";
    public const string RoseOttoId = "rose-otto-kazanlak";
    public const string SandalwoodId = "sandalwood-mysore";

    private readonly List<Material> _materials =
    [
        Build(BergamotId, "Bergamot oil", ExtractionMethod.ColdExpression, 1.0, 0.8, 40m, 100m, (Facet.Citrus, 0.9), (Facet.Aromatic, 0.3)),
        Build(RoseOttoId, "Rose otto", ExtractionMethod.SteamDistillation, 4.0, 6.0, 2400m, 10m, (Facet.Floral, 1.0), (Facet.Spicy, 0.3)),
        Build(SandalwoodId, "Sandalwood oil", ExtractionMethod.SteamDistillation, 0.8, 80.0, 150m, 100m, (Facet.Woody, 0.9), (Facet.Balsamic, 0.5)),
    ];

    public const string LavenderId = "lavender-haute-provence";

    /// <summary>The same catalog plus lavender oil, which the fake extraction recipe makes.</summary>
    public static FakeMaterialCatalog WithLavender()
    {
        var catalog = new FakeMaterialCatalog();
        catalog._materials.Add(Build(
            LavenderId, "Lavender oil", ExtractionMethod.SteamDistillation, 1.1, 1.7, 35m, 100m, (Facet.Aromatic, 0.8), (Facet.Floral, 0.5)));
        return catalog;
    }

    public IReadOnlyList<Material> GetAll() => _materials;

    public Material? Find(MaterialId id) => _materials.Find(material => material.Id == id);

    public void Remove(string id) => _materials.RemoveAll(material => material.Id.Value == id);

    private static Material Build(
        string id,
        string name,
        ExtractionMethod method,
        double strength,
        double halfLife,
        decimal cost,
        decimal stockPercent,
        params (Facet Facet, double Weight)[] facets) =>
        Material.Create(
            MaterialId.Create(id).Value,
            name,
            "Test botanical",
            "Test origin",
            method,
            Facets.Create(facets.Select(f => KeyValuePair.Create(f.Facet, f.Weight))).Value,
            strength,
            Volatility.FromHalfLifeHours(halfLife).Value,
            Money.Create(cost).Value,
            Dilution.FromPercent(stockPercent).Value).Value;
}
