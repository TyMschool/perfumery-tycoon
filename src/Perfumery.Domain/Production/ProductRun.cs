using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Production;

public readonly record struct ProductRunId(Guid Value)
{
    public static ProductRunId New() => new(Guid.NewGuid());
}

public enum ProductRunStatus
{
    Macerating,
    Bottled,
}

/// <param name="Units">Full flacons the run fills.</param>
/// <param name="Loss">Product not bottled: filter loss plus what is left after the last full flacon.</param>
public sealed record BottlingPlan(int Units, Mass Loss);

/// <summary>
/// Concentrate diluted to a product's strength, resting in a maceration vessel until it
/// is filtered and bottled. Once bottled its units belong to the inventory's shelf.
/// </summary>
public sealed class ProductRun : AggregateRoot<ProductRunId>
{
    private ProductRun(
        ProductRunId id,
        ConcentrateBatchId batchId,
        FormulaRevisionRef formula,
        Composition composition,
        ProductTypeId productTypeId,
        Concentration concentration,
        Mass concentrateMass,
        Mass productMass,
        Money cost,
        JobTiming timing)
        : base(id)
    {
        BatchId = batchId;
        Formula = formula;
        Composition = composition;
        ProductTypeId = productTypeId;
        Concentration = concentration;
        ConcentrateMass = concentrateMass;
        ProductMass = productMass;
        Cost = cost;
        Timing = timing;
    }

    public ConcentrateBatchId BatchId { get; }

    public FormulaRevisionRef Formula { get; }

    public Composition Composition { get; }

    public ProductTypeId ProductTypeId { get; }

    public Concentration Concentration { get; }

    public Mass ConcentrateMass { get; }

    /// <summary>Concentrate plus dilution spirit.</summary>
    public Mass ProductMass { get; }

    /// <summary>Concentrate share plus dilution spirit; flacons are added at bottling.</summary>
    public Money Cost { get; private set; }

    public JobTiming Timing { get; }

    public ProductRunStatus Status { get; private set; }

    public int Units { get; private set; }

    public Money UnitCost { get; private set; } = Money.Zero;

    public Mass Loss { get; private set; } = Mass.Zero;

    public bool OccupiesVessel => Status == ProductRunStatus.Macerating;

    /// <summary>Product mass P = m ÷ c for concentrate mass m at concentration c.</summary>
    public static Mass ProductMassFor(Mass concentrateMass, Concentration concentration) =>
        Mass.FromGrams(concentrateMass.Grams / concentration.Fraction).Value;

    /// <summary>Whether a run of this size and strength is acceptable for the product and the vessel.</summary>
    public static Result Check(ProductType productType, Concentration concentration, Mass concentrateMass, Mass vesselCapacity)
    {
        ArgumentNullException.ThrowIfNull(productType);

        if (concentrateMass.IsZero)
        {
            return MassErrors.ZeroNotAllowed;
        }

        if (!productType.Allows(concentration))
        {
            return ProductionErrors.ConcentrationOutOfRange(productType);
        }

        var productMass = ProductMassFor(concentrateMass, concentration);
        return productMass > vesselCapacity
            ? ProductionErrors.OverCapacity(productMass, vesselCapacity)
            : Result.Success();
    }

    public static Result<ProductRun> Start(
        ConcentrateBatch batch,
        ProductType productType,
        Concentration concentration,
        Mass concentrateMass,
        Money cost,
        Mass vesselCapacity,
        DateTimeOffset now,
        TimeSpan maceration)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var check = Check(productType, concentration, concentrateMass, vesselCapacity);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var run = new ProductRun(
            ProductRunId.New(),
            batch.Id,
            batch.Formula,
            batch.Composition,
            productType.Id,
            concentration,
            concentrateMass,
            ProductMassFor(concentrateMass, concentration),
            cost,
            JobTiming.Starting(now, maceration));
        run.Raise(new ProductRunStartedDomainEvent(run.Id, run.ProductTypeId, run.Timing.CompletesAt));
        return run;
    }

    /// <summary>units = ⌊P·(1 − λ) ÷ f⌋ for product mass P, filter loss λ and flacon fill f.</summary>
    public static BottlingPlan PlanBottling(Mass productMass, decimal filterLoss, Mass flaconFill)
    {
        var filtered = productMass.Times(1m - filterLoss);
        var units = (int)decimal.Floor(filtered.Grams / flaconFill.Grams);
        return new BottlingPlan(units, productMass - flaconFill.Times(units));
    }

    public BottlingPlan PlanBottling(decimal filterLoss, Mass flaconFill) =>
        PlanBottling(ProductMass, filterLoss, flaconFill);

    /// <summary>Whether the run can be bottled now.</summary>
    public Result CheckBottling(DateTimeOffset now)
    {
        if (Status == ProductRunStatus.Bottled)
        {
            return ProductionErrors.AlreadyBottled;
        }

        return Timing.IsComplete(now) ? Result.Success() : ProductionErrors.NotReady(Timing.CompletesAt);
    }

    /// <summary>
    /// Filters and fills. The cost of everything, including what was lost, is spread over
    /// the units that exist. Returns the lot for the shelf.
    /// </summary>
    public Result<ShelfLot> Bottle(BottlingPlan plan, Money flaconCost, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var check = CheckBottling(now);
        if (check.IsFailure)
        {
            return check.Error;
        }

        if (plan.Units < 1)
        {
            return ProductionErrors.NothingToBottle;
        }

        Status = ProductRunStatus.Bottled;
        Cost += flaconCost;
        Units = plan.Units;
        Loss = plan.Loss;
        UnitCost = Money.Create(Cost.Amount / plan.Units).Value;
        Raise(new ProductRunBottledDomainEvent(Id, ProductTypeId, Units));

        return new ShelfLot(Id, Formula, Composition, ProductTypeId, Concentration, Units, UnitCost, now);
    }

    public ProductRunSnapshot ToSnapshot() =>
        new(
            Id.Value,
            BatchId.Value,
            Formula.FormulaId.Value,
            Formula.Revision,
            CompositionSnapshot.From(Composition),
            ProductTypeId.Value,
            Concentration.Fraction,
            ConcentrateMass.Grams,
            ProductMass.Grams,
            Cost.Amount,
            Timing.StartedAt,
            Timing.CompletesAt,
            Status,
            Units,
            UnitCost.Amount,
            Loss.Grams);

    public static Result<ProductRun> Restore(ProductRunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var formula = FormulaRevisionRef.Create(new FormulaId(snapshot.FormulaId), snapshot.FormulaRevision);
        var composition = snapshot.Composition.ToComposition();
        var productType = ProductTypeId.Create(snapshot.ProductTypeId);
        var concentration = Concentration.FromFraction(snapshot.Concentration);
        var concentrate = Mass.FromGrams(snapshot.ConcentrateGrams);
        var product = Mass.FromGrams(snapshot.ProductGrams);
        var cost = Money.Create(snapshot.Cost);
        var unitCost = Money.Create(snapshot.UnitCost);
        var loss = Mass.FromGrams(snapshot.LossGrams);
        var timing = JobTiming.Create(snapshot.StartedAt, snapshot.CompletesAt);
        if (Result.FirstFailure(formula, composition, productType, concentration, concentrate, product, cost, unitCost, loss, timing)
            is { } failure)
        {
            return failure;
        }

        if (!Enum.IsDefined(snapshot.Status)
            || concentrate.Value.IsZero
            || product.Value < concentrate.Value
            || snapshot.Units < 0
            || (snapshot.Status == ProductRunStatus.Bottled) != (snapshot.Units > 0))
        {
            return ProductionErrors.CorruptState;
        }

        return new ProductRun(
            new ProductRunId(snapshot.Id),
            new ConcentrateBatchId(snapshot.BatchId),
            formula.Value,
            composition.Value,
            productType.Value,
            concentration.Value,
            concentrate.Value,
            product.Value,
            cost.Value,
            timing.Value)
        {
            Status = snapshot.Status,
            Units = snapshot.Units,
            UnitCost = unitCost.Value,
            Loss = loss.Value,
        };
    }
}

public sealed record ProductRunSnapshot(
    Guid Id,
    Guid BatchId,
    Guid FormulaId,
    int FormulaRevision,
    CompositionSnapshot Composition,
    string ProductTypeId,
    decimal Concentration,
    decimal ConcentrateGrams,
    decimal ProductGrams,
    decimal Cost,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletesAt,
    ProductRunStatus Status,
    int Units,
    decimal UnitCost,
    decimal LossGrams);
