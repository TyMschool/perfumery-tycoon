using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Domain.Common;
using Perfumery.Domain.Stock;

namespace Perfumery.Infrastructure.Stock;

internal sealed class JsonSupplyCatalog : ISupplyCatalog
{
    private const string Era1ResourceName = "Perfumery.Infrastructure.Stock.supplies.era1.json";

    private readonly IReadOnlyList<Supply> _supplies;
    private readonly Dictionary<SupplyId, Supply> _byId;

    public JsonSupplyCatalog()
        : this(EmbeddedResources.Open(Era1ResourceName), Era1ResourceName)
    {
    }

    internal JsonSupplyCatalog(Stream json, string sourceName)
    {
        _supplies =
        [
            .. ReferenceData.LoadArray<SupplyDto>(json, sourceName).Select(dto => ReferenceData.Require(
                Supply.Create(
                    ReferenceData.Require(SupplyId.Create(dto.Id), sourceName, dto.Id),
                    dto.Name,
                    dto.Kind,
                    dto.Unit,
                    ReferenceData.Require(Money.Create(dto.Price), sourceName, dto.Id)),
                sourceName,
                dto.Id)),
        ];
        _byId = ReferenceData.Index(_supplies, supply => supply.Id, sourceName);

        var solvents = _supplies.Where(supply => supply.Kind == SupplyKind.Solvent).ToArray();
        Spirit = solvents is [var spirit] && spirit.Unit == StockUnit.Kilogram
            ? spirit
            : throw new InvalidOperationException($"{sourceName} must list exactly one solvent, sold by the kilogram.");
    }

    public Supply Spirit { get; }

    public IReadOnlyList<Supply> GetAll() => _supplies;

    public Supply? Find(SupplyId id) => _byId.GetValueOrDefault(id);

    private sealed record SupplyDto(string Id, string Name, SupplyKind Kind, StockUnit Unit, decimal Price);
}
