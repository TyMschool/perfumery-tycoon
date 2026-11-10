using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Production;

public readonly record struct ConcentrateBatchId(Guid Value)
{
    public static ConcentrateBatchId New() => new(Guid.NewGuid());
}

public enum ConcentrateStatus
{
    Maturing,
    Ready,
    Exhausted,
}

/// <summary>
/// A formula revision compounded into concentrate. It matures before it can be drawn
/// from, and can then feed several product runs until it is used up.
/// </summary>
public sealed class ConcentrateBatch : AggregateRoot<ConcentrateBatchId>
{
    private ConcentrateBatch(
        ConcentrateBatchId id,
        FormulaRevisionRef formula,
        Composition composition,
        Mass massMade,
        Money totalCost,
        JobTiming timing)
        : base(id)
    {
        Formula = formula;
        Composition = composition;
        MassMade = massMade;
        MassRemaining = massMade;
        TotalCost = totalCost;
        CostRemaining = totalCost;
        Timing = timing;
    }

    public FormulaRevisionRef Formula { get; }

    public Composition Composition { get; }

    public Mass MassMade { get; }

    public Mass MassRemaining { get; private set; }

    /// <summary>What everything weighed into the batch had cost.</summary>
    public Money TotalCost { get; }

    /// <summary>The cost still carried by what remains.</summary>
    public Money CostRemaining { get; private set; }

    public JobTiming Timing { get; }

    public bool IsExhausted => MassRemaining.IsZero;

    public static Result<ConcentrateBatch> Compound(
        FormulaRevisionRef formula,
        Composition composition,
        Mass mass,
        Money cost,
        DateTimeOffset now,
        TimeSpan maturation)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(composition);

        if (mass.IsZero)
        {
            return MassErrors.ZeroNotAllowed;
        }

        var batch = new ConcentrateBatch(
            ConcentrateBatchId.New(), formula, composition, mass, cost, JobTiming.Starting(now, maturation));
        batch.Raise(new ConcentrateCompoundedDomainEvent(batch.Id, formula, mass.Grams));
        return batch;
    }

    /// <summary>Derived from stored state and the time; nothing has to tick.</summary>
    public ConcentrateStatus StatusAt(DateTimeOffset now) =>
        IsExhausted ? ConcentrateStatus.Exhausted
        : Timing.IsComplete(now) ? ConcentrateStatus.Ready
        : ConcentrateStatus.Maturing;

    /// <summary>Whether <see cref="Draw"/> would succeed.</summary>
    public Result CheckDraw(Mass mass, DateTimeOffset now)
    {
        if (mass.IsZero)
        {
            return MassErrors.ZeroNotAllowed;
        }

        if (!Timing.IsComplete(now))
        {
            return ProductionErrors.NotReady(Timing.CompletesAt);
        }

        return mass > MassRemaining ? ProductionErrors.NotEnoughConcentrate(mass, MassRemaining) : Result.Success();
    }

    /// <summary>Takes concentrate out and returns the share of the batch's cost that goes with it.</summary>
    public Result<Money> Draw(Mass mass, DateTimeOffset now)
    {
        var check = CheckDraw(mass, now);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var cost = mass == MassRemaining
            ? CostRemaining
            : CostRemaining.Share(mass.Grams, MassRemaining.Grams);

        MassRemaining -= mass;
        CostRemaining = CostRemaining.Minus(cost);
        return cost;
    }

    public ConcentrateBatchSnapshot ToSnapshot() =>
        new(
            Id.Value,
            Formula.FormulaId.Value,
            Formula.Revision,
            CompositionSnapshot.From(Composition),
            MassMade.Grams,
            MassRemaining.Grams,
            TotalCost.Amount,
            CostRemaining.Amount,
            Timing.StartedAt,
            Timing.CompletesAt);

    public static Result<ConcentrateBatch> Restore(ConcentrateBatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var formula = FormulaRevisionRef.Create(new FormulaId(snapshot.FormulaId), snapshot.FormulaRevision);
        var composition = snapshot.Composition.ToComposition();
        var made = Mass.FromGrams(snapshot.MadeGrams);
        var remaining = Mass.FromGrams(snapshot.RemainingGrams);
        var total = Money.Create(snapshot.TotalCost);
        var costRemaining = Money.Create(snapshot.CostRemaining);
        var timing = JobTiming.Create(snapshot.StartedAt, snapshot.CompletesAt);
        if (Result.FirstFailure(formula, composition, made, remaining, total, costRemaining, timing) is { } failure)
        {
            return failure;
        }

        if (made.Value.IsZero || remaining.Value > made.Value || costRemaining.Value.Amount > total.Value.Amount)
        {
            return ProductionErrors.CorruptState;
        }

        return new ConcentrateBatch(
            new ConcentrateBatchId(snapshot.Id), formula.Value, composition.Value, made.Value, total.Value, timing.Value)
        {
            MassRemaining = remaining.Value,
            CostRemaining = costRemaining.Value,
        };
    }
}

public sealed record ConcentrateBatchSnapshot(
    Guid Id,
    Guid FormulaId,
    int FormulaRevision,
    CompositionSnapshot Composition,
    decimal MadeGrams,
    decimal RemainingGrams,
    decimal TotalCost,
    decimal CostRemaining,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletesAt);
