using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Production;

// The workflows that touch more than one aggregate. Each is pure: aggregates and
// reference data come in as arguments, and nothing here reaches for a session, a
// catalog or a clock. Each checks everything first and only then changes anything,
// so a failure leaves every aggregate as it was.

public static class PurchasingService
{
    /// <summary>Pays for supplies and puts them in stock. Delivery is instant.</summary>
    public static Result Purchase(
        Atelier atelier,
        Inventory inventory,
        StockItemId item,
        Quantity quantity,
        Money unitPrice,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(atelier);
        ArgumentNullException.ThrowIfNull(inventory);

        if (quantity.IsZero)
        {
            return StockErrors.QuantityZero;
        }

        var total = unitPrice.Times(quantity.Value).ToCentimes();
        var paid = atelier.Spend(total, LedgerCategory.SuppliesPurchased, $"{quantity} × {item}", now);
        if (paid.IsFailure)
        {
            return paid;
        }

        atelier.NoteSuppliesPurchased(item.ToString(), quantity.Value, total.Amount);
        return inventory.Receive(item, quantity, total);
    }
}

public static class ExtractionService
{
    /// <summary>What a charge of <paramref name="rawMass"/> takes from stock.</summary>
    public static IReadOnlyList<StockRequirement> Requirements(ExtractionRecipe recipe, Mass rawMass, StockItemId spirit)
    {
        var charge = recipe.ChargeFor(rawMass);
        var requirements = new List<StockRequirement> { new(StockItemId.For(recipe.RawBotanical), Quantity.Of(charge.Raw)) };
        if (!charge.Spirit.IsZero)
        {
            requirements.Add(new StockRequirement(spirit, Quantity.Of(charge.Spirit)));
        }

        return requirements;
    }

    public static Result<ExtractionRun> Start(
        Inventory inventory,
        ExtractionRecipe recipe,
        Mass rawMass,
        AtelierSettings settings,
        int equipmentInUse,
        StockItemId spirit,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(settings);

        var capacity = settings.Equipment[recipe.Equipment].Capacity;
        if (Result.FirstFailure(
                ExtractionRun.Check(recipe, rawMass, capacity),
                settings.CheckFree(recipe.Equipment, equipmentInUse)) is { } failure)
        {
            return failure;
        }

        var consumed = inventory.Reserve(Requirements(recipe, rawMass, spirit));
        if (consumed.IsFailure)
        {
            return consumed.Error;
        }

        return ExtractionRun.Start(recipe, rawMass, capacity, consumed.Value.TotalCost, now, settings.Scale(recipe.Duration));
    }

    /// <summary>Empties the equipment into stock at what the inputs had cost.</summary>
    public static Result<ExtractionOutput> Collect(ExtractionRun run, Inventory inventory, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(inventory);

        var output = run.Collect(now);
        if (output.IsFailure)
        {
            return output;
        }

        inventory.Receive(StockItemId.For(output.Value.MaterialId), Quantity.Of(output.Value.Mass), output.Value.Cost);
        return output;
    }
}

/// <param name="Requirements">What to weigh out of stock: each material neat, plus carrier spirit.</param>
/// <param name="Mass">The mass the batch will actually have: the sum of what is weighed.</param>
public sealed record CompoundingPlan(IReadOnlyList<StockRequirement> Requirements, Mass Mass);

public static class CompoundingService
{
    /// <summary>
    /// For a batch of mass M: each material mᵢ = M·nᵢ, and carrier = M·v, taken as what is
    /// left of M so that the weighed masses add up exactly.
    /// </summary>
    public static Result<CompoundingPlan> Plan(Composition composition, Mass mass, StockItemId spirit)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (mass.IsZero)
        {
            return MassErrors.ZeroNotAllowed;
        }

        var requirements = new List<StockRequirement>();
        var weighed = Mass.Zero;
        foreach (var line in composition.Lines)
        {
            var neat = mass.Times(line.NeatFraction);
            if (neat.IsZero)
            {
                return ProductionErrors.BatchTooSmall;
            }

            requirements.Add(new StockRequirement(StockItemId.For(line.MaterialId), Quantity.Of(neat)));
            weighed += neat;
        }

        if (mass > weighed)
        {
            var carrier = mass - weighed;
            requirements.Add(new StockRequirement(spirit, Quantity.Of(carrier)));
            weighed += carrier;
        }

        return new CompoundingPlan(requirements, weighed);
    }

    public static Result<ConcentrateBatch> Compound(
        Inventory inventory,
        FormulaRevisionRef formula,
        Composition composition,
        Mass mass,
        AtelierSettings settings,
        StockItemId spirit,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(settings);

        var plan = Plan(composition, mass, spirit);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var consumed = inventory.Reserve(plan.Value.Requirements);
        if (consumed.IsFailure)
        {
            return consumed.Error;
        }

        return ConcentrateBatch.Compound(
            formula,
            composition,
            plan.Value.Mass,
            consumed.Value.TotalCost,
            now,
            settings.Scale(settings.ConcentrateMaturation));
    }
}

public static class BottlingService
{
    /// <summary>Dilution spirit for a run: P − m.</summary>
    public static StockRequirement DilutionSpirit(Mass concentrateMass, Concentration concentration, StockItemId spirit) =>
        new(spirit, Quantity.Of(ProductRun.ProductMassFor(concentrateMass, concentration) - concentrateMass));

    public static Result<ProductRun> StartRun(
        Inventory inventory,
        ConcentrateBatch batch,
        ProductType productType,
        Concentration concentration,
        Mass concentrateMass,
        AtelierSettings settings,
        int vesselsInUse,
        StockItemId spirit,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(settings);

        var capacity = settings.Equipment[EquipmentKind.MacerationVessel].Capacity;
        if (Result.FirstFailure(
                ProductRun.Check(productType, concentration, concentrateMass, capacity),
                settings.CheckFree(EquipmentKind.MacerationVessel, vesselsInUse),
                batch.CheckDraw(concentrateMass, now)) is { } failure)
        {
            return failure;
        }

        var dilution = inventory.Reserve([DilutionSpirit(concentrateMass, concentration, spirit)]);
        if (dilution.IsFailure)
        {
            return dilution.Error;
        }

        var concentrateCost = batch.Draw(concentrateMass, now).Value;
        return ProductRun.Start(
            batch,
            productType,
            concentration,
            concentrateMass,
            concentrateCost + dilution.Value.TotalCost,
            capacity,
            now,
            settings.Scale(productType.Maceration));
    }

    /// <summary>Filters, fills flacons from stock and puts the units on the shelf.</summary>
    public static Result<ShelfLot> Bottle(
        ProductRun run,
        Inventory inventory,
        ProductType productType,
        AtelierSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(productType);
        ArgumentNullException.ThrowIfNull(settings);

        var ready = run.CheckBottling(now);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var plan = run.PlanBottling(settings.FilterLoss, productType.FlaconFill);
        if (plan.Units < 1)
        {
            return ProductionErrors.NothingToBottle;
        }

        var flacons = inventory.Reserve([new StockRequirement(StockItemId.For(productType.Flacon), Quantity.Units(plan.Units))]);
        if (flacons.IsFailure)
        {
            return flacons.Error;
        }

        var lot = run.Bottle(plan, flacons.Value.TotalCost, now);
        inventory.AddShelfLot(lot.Value);
        return lot;
    }
}
