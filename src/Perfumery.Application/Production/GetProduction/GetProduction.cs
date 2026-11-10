using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Production.GetProduction;

public sealed record GetProductionQuery : IQuery<Result<ProductionResponse>>;

public sealed record ProductionResponse(
    IReadOnlyList<ExtractionRunResponse> ExtractionRuns,
    IReadOnlyList<ConcentrateBatchResponse> ConcentrateBatches,
    IReadOnlyList<ProductRunResponse> ProductRuns);

/// <param name="CompletesAt">When the job is done.</param>
/// <param name="RemainingSeconds">Time left at the moment of the query; the UI counts down from here.</param>
/// <param name="IsComplete">Whether the next action is available.</param>
public sealed record JobTimingResponse(DateTimeOffset StartedAt, DateTimeOffset CompletesAt, double RemainingSeconds, bool IsComplete);

public sealed record ExtractionRunResponse(
    Guid Id,
    string RecipeId,
    string MaterialId,
    string MaterialName,
    string Equipment,
    decimal RawGrams,
    decimal OutputGrams,
    JobTimingResponse Timing);

public sealed record ConcentrateBatchResponse(
    Guid Id,
    Guid FormulaId,
    string FormulaName,
    int FormulaRevision,
    decimal MadeGrams,
    decimal RemainingGrams,
    decimal TotalCost,
    string Status,
    JobTimingResponse Timing);

public sealed record ProductRunResponse(
    Guid Id,
    Guid BatchId,
    Guid FormulaId,
    string FormulaName,
    string ProductTypeId,
    string ProductName,
    decimal ConcentrationPercent,
    decimal ProductGrams,
    decimal Cost,
    JobTimingResponse Timing);

internal sealed class GetProductionQueryHandler(
    IGameSession session,
    IGameClock clock,
    IMaterialCatalog materials,
    IProductTypeCatalog productTypes)
    : IQueryHandler<GetProductionQuery, Result<ProductionResponse>>
{
    public Task<Result<ProductionResponse>> Handle(GetProductionQuery query, CancellationToken cancellationToken)
    {
        if (!session.IsStarted)
        {
            return Task.FromResult<Result<ProductionResponse>>(GameErrors.NotStarted);
        }

        var now = session.Atelier.EffectiveNow(clock.UtcNow);

        var extractions = session.ExtractionRuns
            .Where(run => run.Status == ExtractionStatus.Running)
            .OrderBy(run => run.Timing.CompletesAt)
            .Select(run => new ExtractionRunResponse(
                run.Id.Value,
                run.RecipeId.Value,
                run.Output.Value,
                materials.Find(run.Output)?.Name ?? run.Output.Value,
                run.Equipment.ToString(),
                run.RawMass.Grams,
                run.OutputMass.Grams,
                Timing(run.Timing, now)));

        var batches = session.ConcentrateBatches
            .OrderBy(batch => batch.Timing.CompletesAt)
            .Select(batch => new ConcentrateBatchResponse(
                batch.Id.Value,
                batch.Formula.FormulaId.Value,
                session.Find(batch.Formula.FormulaId)?.Name ?? string.Empty,
                batch.Formula.Revision,
                batch.MassMade.Grams,
                batch.MassRemaining.Grams,
                batch.TotalCost.ToCentimes().Amount,
                batch.StatusAt(now).ToString(),
                Timing(batch.Timing, now)));

        var runs = session.ProductRuns
            .Where(run => run.Status == ProductRunStatus.Macerating)
            .OrderBy(run => run.Timing.CompletesAt)
            .Select(run => new ProductRunResponse(
                run.Id.Value,
                run.BatchId.Value,
                run.Formula.FormulaId.Value,
                session.Find(run.Formula.FormulaId)?.Name ?? string.Empty,
                run.ProductTypeId.Value,
                productTypes.Find(run.ProductTypeId)?.Name ?? run.ProductTypeId.Value,
                run.Concentration.Percent,
                run.ProductMass.Grams,
                run.Cost.ToCentimes().Amount,
                Timing(run.Timing, now)));

        return Task.FromResult<Result<ProductionResponse>>(new ProductionResponse([.. extractions], [.. batches], [.. runs]));
    }

    private static JobTimingResponse Timing(JobTiming timing, DateTimeOffset now) =>
        new(timing.StartedAt, timing.CompletesAt, timing.Remaining(now).TotalSeconds, timing.IsComplete(now));
}
