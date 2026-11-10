using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Game;

internal static class GameSessionExtensions
{
    public static int EquipmentInUse(this IGameSession session, EquipmentKind kind) =>
        kind == EquipmentKind.MacerationVessel
            ? session.ProductRuns.Count(run => run.OccupiesVessel)
            : session.ExtractionRuns.Count(run => run.OccupiesEquipment && run.Equipment == kind);
}

/// <param name="Name">Display name.</param>
/// <param name="Unit">Kilograms or pieces.</param>
/// <param name="Price">Catalog price per unit.</param>
internal sealed record StockItemInfo(string Name, StockUnit Unit, Money Price);

/// <summary>Looks a stock item up in whichever catalog it belongs to.</summary>
internal sealed class StockItemDirectory(IMaterialCatalog materials, ISupplyCatalog supplies)
{
    public StockItemId Spirit => StockItemId.For(supplies.Spirit.Id);

    public Money SpiritPrice => supplies.Spirit.Price;

    public StockItemInfo? Find(StockItemId item)
    {
        if (item.Kind == StockItemKind.Material)
        {
            var id = MaterialId.Create(item.Slug);
            return id.IsSuccess && materials.Find(id.Value) is { } material
                ? new StockItemInfo(material.Name, StockUnit.Kilogram, material.CostPerKg)
                : null;
        }

        var supplyId = SupplyId.Create(item.Slug);
        return supplyId.IsSuccess && supplies.Find(supplyId.Value) is { } supply
            ? new StockItemInfo(supply.Name, supply.Unit, supply.Price)
            : null;
    }
}

/// <summary>Rebuilds aggregates from a loaded snapshot and checks every ID against reference data.</summary>
internal sealed class GameRestorer(
    IMaterialCatalog materials,
    StockItemDirectory items,
    IExtractionRecipeCatalog recipes,
    IProductTypeCatalog productTypes)
{
    public Result<GameState> Restore(GameSnapshot snapshot)
    {
        var atelier = Atelier.Restore(snapshot.Atelier);
        var inventory = Inventory.Restore(snapshot.Inventory);
        var formulas = RestoreAll(snapshot.Formulas, Formula.Restore);
        var extractionRuns = RestoreAll(snapshot.ExtractionRuns, ExtractionRun.Restore);
        var batches = RestoreAll(snapshot.ConcentrateBatches, ConcentrateBatch.Restore);
        var productRuns = RestoreAll(snapshot.ProductRuns, ProductRun.Restore);

        if (Result.FirstFailure(atelier, inventory, formulas, extractionRuns, batches, productRuns) is { } failure)
        {
            return SaveErrors.Invalid(failure);
        }

        var state = new GameState(
            atelier.Value, inventory.Value, formulas.Value, extractionRuns.Value, batches.Value, productRuns.Value);

        return UnknownReference(state) is { } unknown ? SaveErrors.UnknownReference(unknown) : state;
    }

    private static Result<IReadOnlyList<T>> RestoreAll<TSnapshot, T>(
        IEnumerable<TSnapshot> snapshots,
        Func<TSnapshot, Result<T>> restore)
    {
        var restored = new List<T>();
        foreach (var snapshot in snapshots)
        {
            var result = restore(snapshot);
            if (result.IsFailure)
            {
                return result.Error;
            }

            restored.Add(result.Value);
        }

        return Result.Success<IReadOnlyList<T>>(restored);
    }

    private string? UnknownReference(GameState state)
    {
        var materialIds = state.Formulas.SelectMany(formula => formula.Lines.Select(line => line.MaterialId))
            .Concat(state.Inventory.Shelf.SelectMany(lot => MaterialsOf(lot.Composition)))
            .Concat(state.ConcentrateBatches.SelectMany(batch => MaterialsOf(batch.Composition)))
            .Concat(state.ProductRuns.SelectMany(run => MaterialsOf(run.Composition)))
            .Concat(state.ExtractionRuns.Select(run => run.Output));

        var unknownMaterial = materialIds.FirstOrDefault(id => materials.Find(id) is null);
        if (unknownMaterial is not null)
        {
            return unknownMaterial.Value;
        }

        var unknownItem = state.Inventory.Lines.Select(line => line.Item).FirstOrDefault(item => items.Find(item) is null);
        if (unknownItem is not null)
        {
            return unknownItem.ToString();
        }

        var unknownRecipe = state.ExtractionRuns.Select(run => run.RecipeId).FirstOrDefault(id => recipes.Find(id) is null);
        if (unknownRecipe is not null)
        {
            return unknownRecipe.Value;
        }

        var unknownProduct = state.ProductRuns.Select(run => run.ProductTypeId)
            .Concat(state.Inventory.Shelf.Select(lot => lot.ProductTypeId))
            .FirstOrDefault(id => productTypes.Find(id) is null);

        return unknownProduct?.Value;
    }

    private static IEnumerable<MaterialId> MaterialsOf(Composition composition) =>
        composition.Lines.Select(line => line.MaterialId);
}
