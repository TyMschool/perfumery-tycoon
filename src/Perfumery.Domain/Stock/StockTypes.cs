using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Stock;

/// <summary>What the inventory holds of one item. The average cost is derived from the total, never stored.</summary>
public sealed record StockLine(StockItemId Item, Quantity Quantity, Money TotalCost)
{
    public Money AverageUnitCost =>
        Quantity.IsZero ? Money.Zero : Money.Create(TotalCost.Amount / Quantity.Value).Value;
}

public sealed record StockRequirement(StockItemId Item, Quantity Quantity);

/// <param name="Item">The item that is short.</param>
/// <param name="Required">How much was asked for.</param>
/// <param name="Available">How much is held.</param>
public sealed record StockShortage(StockItemId Item, Quantity Required, Quantity Available)
{
    public decimal Missing => Required.Value - Available.Value;
}

/// <summary>A reservation failed. Lists every shortage, not just the first.</summary>
public sealed record InsufficientStockError(IReadOnlyList<StockShortage> Shortages)
    : Error(
        "Inventory.InsufficientStock",
        $"Not enough stock of: {string.Join(", ", Shortages.Select(shortage => shortage.Item))}.",
        ErrorType.Conflict);

public sealed record ConsumedStock(StockItemId Item, Quantity Quantity, Money Cost);

/// <summary>What a successful reservation took out of stock, and what it had cost.</summary>
public sealed record StockConsumption(IReadOnlyList<ConsumedStock> Lines)
{
    public Money TotalCost => Lines.Aggregate(Money.Zero, (total, line) => total + line.Cost);
}

/// <summary>
/// Bottled units of one product run, on the shelf. Carries everything Phase 3 needs to
/// sell and score them, so the run itself can be forgotten.
/// </summary>
public sealed record ShelfLot(
    ProductRunId ProductRunId,
    FormulaRevisionRef Formula,
    Composition Composition,
    ProductTypeId ProductTypeId,
    Concentration Concentration,
    int Units,
    Money UnitCost,
    DateTimeOffset BottledAt);

public static class StockErrors
{
    public static readonly Error ItemIdMalformed =
        Error.Validation("Stock.ItemIdMalformed", "A stock item ID is 'material:<slug>' or 'supply:<slug>'.");

    public static readonly Error QuantityNegative =
        Error.Validation("Stock.QuantityNegative", "A quantity cannot be negative.");

    public static readonly Error QuantityZero =
        Error.Validation("Stock.QuantityZero", "The quantity must be above zero.");

    public static readonly Error QuantityNotWhole =
        Error.Validation("Stock.QuantityNotWhole", "Items sold by the piece come in whole numbers.");

    public static readonly Error DuplicateShelfLot =
        Error.Conflict("Stock.DuplicateShelfLot", "That product run already has a lot on the shelf.");

    public static readonly Error EmptyShelfLot =
        Error.Validation("Stock.EmptyShelfLot", "A shelf lot holds at least one unit.");

    public static readonly Error CorruptLine =
        Error.Validation("Stock.CorruptLine", "A saved stock line is duplicated or has no quantity.");

    public static Error ItemNotFound(StockItemId item) =>
        Error.NotFound("Stock.ItemNotFound", $"'{item}' is not something the atelier can stock.");
}
