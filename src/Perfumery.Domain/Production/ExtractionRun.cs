using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Production;

public readonly record struct ExtractionRunId(Guid Value)
{
    public static ExtractionRunId New() => new(Guid.NewGuid());
}

public enum ExtractionStatus
{
    Running,
    Collected,
}

/// <param name="MaterialId">The material made.</param>
/// <param name="Mass">How much of it.</param>
/// <param name="Cost">What its inputs cost in total.</param>
public sealed record ExtractionOutput(MaterialId MaterialId, Mass Mass, Money Cost);

/// <summary>
/// One charge of a still or tincture jar. It occupies the equipment from the start until
/// it is collected, not merely until it completes.
/// </summary>
public sealed class ExtractionRun : AggregateRoot<ExtractionRunId>
{
    private ExtractionRun(
        ExtractionRunId id,
        ExtractionRecipeId recipeId,
        MaterialId output,
        EquipmentKind equipment,
        Mass rawMass,
        Mass outputMass,
        Money inputCost,
        JobTiming timing)
        : base(id)
    {
        RecipeId = recipeId;
        Output = output;
        Equipment = equipment;
        RawMass = rawMass;
        OutputMass = outputMass;
        InputCost = inputCost;
        Timing = timing;
    }

    public ExtractionRecipeId RecipeId { get; }

    public MaterialId Output { get; }

    public EquipmentKind Equipment { get; }

    public Mass RawMass { get; }

    public Mass OutputMass { get; }

    public Money InputCost { get; }

    public JobTiming Timing { get; }

    public ExtractionStatus Status { get; private set; }

    public bool OccupiesEquipment => Status == ExtractionStatus.Running;

    /// <summary>
    /// Whether a charge is acceptable. The whole charge, raw botanical plus any spirit,
    /// must fit the equipment.
    /// </summary>
    public static Result Check(ExtractionRecipe recipe, Mass rawMass, Mass capacity)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        if (rawMass.IsZero)
        {
            return MassErrors.ZeroNotAllowed;
        }

        var charge = recipe.ChargeFor(rawMass);
        if (charge.Total > capacity)
        {
            return ProductionErrors.OverCapacity(charge.Total, capacity);
        }

        return charge.Output.IsZero ? ExtractionErrors.ChargeTooSmall : Result.Success();
    }

    public static Result<ExtractionRun> Start(
        ExtractionRecipe recipe,
        Mass rawMass,
        Mass capacity,
        Money inputCost,
        DateTimeOffset now,
        TimeSpan duration)
    {
        var check = Check(recipe, rawMass, capacity);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var run = new ExtractionRun(
            ExtractionRunId.New(),
            recipe.Id,
            recipe.Output,
            recipe.Equipment,
            rawMass,
            recipe.ChargeFor(rawMass).Output,
            inputCost,
            JobTiming.Starting(now, duration));
        run.Raise(new ExtractionStartedDomainEvent(run.Id, run.RecipeId, run.Timing.CompletesAt));
        return run;
    }

    public bool IsComplete(DateTimeOffset now) => Timing.IsComplete(now);

    /// <summary>Whether <see cref="Collect"/> would succeed.</summary>
    public Result CheckCollectable(DateTimeOffset now)
    {
        if (Status == ExtractionStatus.Collected)
        {
            return ExtractionErrors.AlreadyCollected;
        }

        return IsComplete(now) ? Result.Success() : ProductionErrors.NotReady(Timing.CompletesAt);
    }

    /// <summary>Empties the equipment and hands over the material with what it cost.</summary>
    public Result<ExtractionOutput> Collect(DateTimeOffset now)
    {
        var check = CheckCollectable(now);
        if (check.IsFailure)
        {
            return check.Error;
        }

        Status = ExtractionStatus.Collected;
        Raise(new ExtractionCollectedDomainEvent(Id, Output, OutputMass.Grams));
        return new ExtractionOutput(Output, OutputMass, InputCost);
    }

    public ExtractionRunSnapshot ToSnapshot() =>
        new(
            Id.Value,
            RecipeId.Value,
            Output.Value,
            Equipment,
            RawMass.Grams,
            OutputMass.Grams,
            InputCost.Amount,
            Timing.StartedAt,
            Timing.CompletesAt,
            Status);

    public static Result<ExtractionRun> Restore(ExtractionRunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var recipeId = ExtractionRecipeId.Create(snapshot.RecipeId);
        var output = MaterialId.Create(snapshot.Output);
        var raw = Mass.FromGrams(snapshot.RawGrams);
        var outputMass = Mass.FromGrams(snapshot.OutputGrams);
        var cost = Money.Create(snapshot.InputCost);
        var timing = JobTiming.Create(snapshot.StartedAt, snapshot.CompletesAt);
        if (Result.FirstFailure(recipeId, output, raw, outputMass, cost, timing) is { } failure)
        {
            return failure;
        }

        if (outputMass.Value.IsZero || !Enum.IsDefined(snapshot.Equipment) || !Enum.IsDefined(snapshot.Status))
        {
            return ProductionErrors.CorruptState;
        }

        return new ExtractionRun(
            new ExtractionRunId(snapshot.Id),
            recipeId.Value,
            output.Value,
            snapshot.Equipment,
            raw.Value,
            outputMass.Value,
            cost.Value,
            timing.Value)
        {
            Status = snapshot.Status,
        };
    }
}

public sealed record ExtractionRunSnapshot(
    Guid Id,
    string RecipeId,
    string Output,
    EquipmentKind Equipment,
    decimal RawGrams,
    decimal OutputGrams,
    decimal InputCost,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletesAt,
    ExtractionStatus Status);
