using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Stock;

public readonly record struct InventoryId(Guid Value)
{
    public static InventoryId New() => new(Guid.NewGuid());
}

/// <summary>
/// Everything the atelier holds: materials and supplies with their cost, and the shelf
/// of bottled product. No quantity is ever negative.
/// </summary>
public sealed class Inventory : AggregateRoot<InventoryId>
{
    private readonly Dictionary<StockItemId, StockLine> _lines = [];
    private readonly List<ShelfLot> _shelf = [];

    private Inventory(InventoryId id)
        : base(id)
    {
    }

    public IReadOnlyList<StockLine> Lines => [.. _lines.Values.OrderBy(line => line.Item.ToString(), StringComparer.Ordinal)];

    public IReadOnlyList<ShelfLot> Shelf => _shelf.AsReadOnly();

    /// <summary>What all stock on hand cost, excluding the shelf.</summary>
    public Money StockValue => _lines.Values.Aggregate(Money.Zero, (total, line) => total + line.TotalCost);

    /// <summary>What all bottled units cost.</summary>
    public Money ShelfValue => _shelf.Aggregate(Money.Zero, (total, lot) => total + lot.UnitCost.Times(lot.Units));

    public static Inventory Create() => new(InventoryId.New());

    public Quantity QuantityOf(StockItemId item) =>
        _lines.TryGetValue(item, out var line) ? line.Quantity : Quantity.Zero;

    public Money AverageUnitCost(StockItemId item) =>
        _lines.TryGetValue(item, out var line) ? line.AverageUnitCost : Money.Zero;

    /// <summary>Adds stock. The line's average cost becomes (Q·A + q·p) ÷ (Q + q).</summary>
    public Result Receive(StockItemId item, Quantity quantity, Money totalCost)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(totalCost);

        if (quantity.IsZero)
        {
            return StockErrors.QuantityZero;
        }

        _lines[item] = _lines.TryGetValue(item, out var line)
            ? line with { Quantity = line.Quantity + quantity, TotalCost = line.TotalCost + totalCost }
            : new StockLine(item, quantity, totalCost);

        return Result.Success();
    }

    /// <summary>Every requirement the stock cannot meet. Empty means a reservation would succeed.</summary>
    public IReadOnlyList<StockShortage> Shortages(IEnumerable<StockRequirement> requirements) =>
    [
        .. Merge(requirements)
            .Where(requirement => requirement.Quantity.Value > QuantityOf(requirement.Item).Value)
            .Select(requirement => new StockShortage(requirement.Item, requirement.Quantity, QuantityOf(requirement.Item))),
    ];

    /// <summary>
    /// Takes everything asked for out of stock at its current average cost, or takes
    /// nothing at all and reports every shortage.
    /// </summary>
    public Result<StockConsumption> Reserve(IEnumerable<StockRequirement> requirements)
    {
        var merged = Merge(requirements);
        var shortages = Shortages(merged);
        if (shortages.Count > 0)
        {
            return new InsufficientStockError(shortages);
        }

        var consumed = new List<ConsumedStock>();
        foreach (var requirement in merged.Where(requirement => !requirement.Quantity.IsZero))
        {
            var line = _lines[requirement.Item];
            var takesAll = requirement.Quantity == line.Quantity;
            var cost = takesAll
                ? line.TotalCost
                : line.TotalCost.Share(requirement.Quantity.Value, line.Quantity.Value);

            if (takesAll)
            {
                _lines.Remove(requirement.Item);
            }
            else
            {
                _lines[requirement.Item] = line with
                {
                    Quantity = Quantity.Create(line.Quantity.Value - requirement.Quantity.Value).Value,
                    TotalCost = line.TotalCost.Minus(cost),
                };
            }

            consumed.Add(new ConsumedStock(requirement.Item, requirement.Quantity, cost));
        }

        return new StockConsumption(consumed);
    }

    /// <summary>Puts a bottled product run on the shelf. One lot per run.</summary>
    public Result AddShelfLot(ShelfLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);

        if (lot.Units < 1)
        {
            return StockErrors.EmptyShelfLot;
        }

        if (_shelf.Exists(existing => existing.ProductRunId == lot.ProductRunId))
        {
            return StockErrors.DuplicateShelfLot;
        }

        _shelf.Add(lot);
        return Result.Success();
    }

    public InventorySnapshot ToSnapshot() =>
        new(
            Id.Value,
            [.. Lines.Select(line => new StockLineSnapshot(line.Item.ToString(), line.Quantity.Value, line.TotalCost.Amount))],
            [.. _shelf.Select(lot => new ShelfLotSnapshot(
                lot.ProductRunId.Value,
                lot.Formula.FormulaId.Value,
                lot.Formula.Revision,
                CompositionSnapshot.From(lot.Composition),
                lot.ProductTypeId.Value,
                lot.Concentration.Fraction,
                lot.Units,
                lot.UnitCost.Amount,
                lot.BottledAt))]);

    /// <summary>Rebuilds the inventory from saved state, re-checking every invariant.</summary>
    public static Result<Inventory> Restore(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var inventory = new Inventory(new InventoryId(snapshot.Id));
        foreach (var line in snapshot.Lines)
        {
            var item = StockItemId.Parse(line.Item);
            var quantity = Quantity.Create(line.Quantity);
            var cost = Money.Create(line.TotalCost);
            if (Result.FirstFailure(item, quantity, cost) is { } failure)
            {
                return failure;
            }

            if (quantity.Value.IsZero || !inventory._lines.TryAdd(item.Value, new StockLine(item.Value, quantity.Value, cost.Value)))
            {
                return StockErrors.CorruptLine;
            }
        }

        foreach (var lot in snapshot.Shelf)
        {
            var formula = FormulaRevisionRef.Create(new FormulaId(lot.FormulaId), lot.FormulaRevision);
            var composition = lot.Composition.ToComposition();
            var productType = ProductTypeId.Create(lot.ProductTypeId);
            var concentration = Concentration.FromFraction(lot.Concentration);
            var unitCost = Money.Create(lot.UnitCost);
            if (Result.FirstFailure(formula, composition, productType, concentration, unitCost) is { } failure)
            {
                return failure;
            }

            var added = inventory.AddShelfLot(new ShelfLot(
                new ProductRunId(lot.ProductRunId),
                formula.Value,
                composition.Value,
                productType.Value,
                concentration.Value,
                lot.Units,
                unitCost.Value,
                lot.BottledAt));
            if (added.IsFailure)
            {
                return added.Error;
            }
        }

        return inventory;
    }

    private static List<StockRequirement> Merge(IEnumerable<StockRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        return
        [
            .. requirements
                .GroupBy(requirement => requirement.Item)
                .Select(group => new StockRequirement(
                    group.Key,
                    group.Aggregate(Quantity.Zero, (total, requirement) => total + requirement.Quantity))),
        ];
    }
}

public sealed record InventorySnapshot(
    Guid Id,
    IReadOnlyList<StockLineSnapshot> Lines,
    IReadOnlyList<ShelfLotSnapshot> Shelf);

public sealed record StockLineSnapshot(string Item, decimal Quantity, decimal TotalCost);

public sealed record ShelfLotSnapshot(
    Guid ProductRunId,
    Guid FormulaId,
    int FormulaRevision,
    CompositionSnapshot Composition,
    string ProductTypeId,
    decimal Concentration,
    int Units,
    decimal UnitCost,
    DateTimeOffset BottledAt);

public sealed record CompositionSnapshot(IReadOnlyList<CompositionLineSnapshot> Lines)
{
    public static CompositionSnapshot From(Composition composition) =>
        new([.. composition.Lines.Select(line => new CompositionLineSnapshot(line.MaterialId.Value, line.NeatFraction))]);

    public Result<Composition> ToComposition()
    {
        var lines = new List<CompositionLine>();
        foreach (var line in Lines)
        {
            var materialId = MaterialId.Create(line.MaterialId);
            if (materialId.IsFailure)
            {
                return materialId.Error;
            }

            lines.Add(new CompositionLine(materialId.Value, line.NeatFraction));
        }

        return Composition.Create(lines);
    }
}

public sealed record CompositionLineSnapshot(string MaterialId, decimal NeatFraction);
