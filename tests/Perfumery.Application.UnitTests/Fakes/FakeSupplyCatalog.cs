using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Domain.Common;
using Perfumery.Domain.Stock;

namespace Perfumery.Application.UnitTests.Fakes;

/// <summary>Spirit at 3 francs per kg, lavender tops, benzoin resin and one flacon.</summary>
internal sealed class FakeSupplyCatalog : ISupplyCatalog
{
    public const string SpiritId = "spirit-of-wine";
    public const string LavenderTopsId = "lavender-flowering-tops";
    public const string BenzoinResinId = "benzoin-resin";
    public const string FlaconId = "flacon-cologne-200g";

    private readonly List<Supply> _supplies =
    [
        Build(SpiritId, "Spirit of wine", SupplyKind.Solvent, StockUnit.Kilogram, 3m),
        Build(LavenderTopsId, "Lavender flowering tops", SupplyKind.RawBotanical, StockUnit.Kilogram, 0.25m),
        Build(BenzoinResinId, "Benzoin resin", SupplyKind.RawBotanical, StockUnit.Kilogram, 30m),
        Build(FlaconId, "Cologne flacon, 200 g", SupplyKind.Packaging, StockUnit.Each, 0.40m),
    ];

    public Supply Spirit => _supplies[0];

    public IReadOnlyList<Supply> GetAll() => _supplies;

    public Supply? Find(SupplyId id) => _supplies.Find(supply => supply.Id == id);

    private static Supply Build(string id, string name, SupplyKind kind, StockUnit unit, decimal price) =>
        Supply.Create(SupplyId.Create(id).Value, name, kind, unit, Money.Create(price).Value).Value;
}
