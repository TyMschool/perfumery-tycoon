using System.Text.Json;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Infrastructure.Materials;

/// <summary>
/// The materials catalog, read once from embedded JSON. The data is curated and ships
/// with the game, so anything invalid in it is a bug and throws at load.
/// </summary>
internal sealed class JsonMaterialCatalog : IMaterialCatalog
{
    private const string Era1ResourceName = "Perfumery.Infrastructure.Materials.materials.era1.json";

    private readonly IReadOnlyList<Material> _materials;
    private readonly Dictionary<MaterialId, Material> _byId;

    public JsonMaterialCatalog()
        : this(EmbeddedResources.Open(Era1ResourceName), Era1ResourceName)
    {
    }

    internal JsonMaterialCatalog(Stream json, string sourceName)
    {
        using (json)
        {
            var dtos = JsonSerializer.Deserialize<MaterialDto[]>(json, EmbeddedResources.JsonOptions)
                ?? throw new InvalidOperationException($"{sourceName} is empty.");

            _materials = [.. dtos.Select(dto => ToMaterial(dto, sourceName))];
        }

        _byId = new Dictionary<MaterialId, Material>();
        foreach (var material in _materials)
        {
            if (!_byId.TryAdd(material.Id, material))
            {
                throw new InvalidOperationException($"{sourceName} lists material '{material.Id}' twice.");
            }
        }
    }

    public IReadOnlyList<Material> GetAll() => _materials;

    public Material? Find(MaterialId id) => _byId.GetValueOrDefault(id);

    private static Material ToMaterial(MaterialDto dto, string sourceName)
    {
        T Require<T>(Result<T> result) => result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(
                $"{sourceName}: material '{dto.Id}' is invalid: {result.Error.Description}");

        return Require(Material.Create(
            Require(MaterialId.Create(dto.Id)),
            dto.Name,
            dto.BotanicalSource,
            dto.Origin,
            dto.ExtractionMethod,
            Require(Facets.Create(dto.Facets ?? [])),
            dto.OdorStrength,
            Require(Volatility.FromHalfLifeHours(dto.HalfLifeHours)),
            Require(Money.Create(dto.CostPerKg)),
            Require(Dilution.FromPercent(dto.StockDilutionPercent))));
    }

    private sealed record MaterialDto(
        string Id,
        string Name,
        string BotanicalSource,
        string Origin,
        ExtractionMethod ExtractionMethod,
        Dictionary<Facet, double>? Facets,
        double OdorStrength,
        double HalfLifeHours,
        decimal CostPerKg,
        decimal StockDilutionPercent);
}
