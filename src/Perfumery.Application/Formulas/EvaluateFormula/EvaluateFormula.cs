using FluentValidation;
using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Evaluation;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Formulas.EvaluateFormula;

public sealed record EvaluateFormulaQuery(Guid FormulaId) : IQuery<Result<ScentProfileResponse>>;

internal sealed class EvaluateFormulaQueryValidator : AbstractValidator<EvaluateFormulaQuery>
{
    public EvaluateFormulaQueryValidator()
    {
        RuleFor(query => query.FormulaId).NotEmpty();
    }
}

internal sealed class EvaluateFormulaQueryHandler(
    IFormulaRepository formulas,
    IMaterialCatalog catalog,
    ISupplyCatalog supplies,
    ScentEvaluator evaluator)
    : IQueryHandler<EvaluateFormulaQuery, Result<ScentProfileResponse>>
{
    public async Task<Result<ScentProfileResponse>> Handle(EvaluateFormulaQuery query, CancellationToken cancellationToken)
    {
        var id = new FormulaId(query.FormulaId);
        var formula = await formulas.GetByIdAsync(id, cancellationToken);
        if (formula is null)
        {
            return FormulaErrors.NotFound(id);
        }

        var materials = new Dictionary<MaterialId, Material>();
        foreach (var line in formula.Lines)
        {
            var material = catalog.Find(line.MaterialId);
            if (material is null)
            {
                return MaterialErrors.NotFound(line.MaterialId);
            }

            materials[material.Id] = material;
        }

        var composition = formula.Compose();
        if (composition.IsFailure)
        {
            return composition.Error;
        }

        var profile = evaluator.Evaluate(composition.Value, materials, supplies.Spirit.Price);
        if (profile.IsFailure)
        {
            return profile.Error;
        }

        return new ScentProfileResponse(
            formula.Id.Value,
            formula.Name,
            formula.Revision,
            profile.Value.CostPerKgOfConcentrate.Amount,
            composition.Value.SolventFraction,
            [.. profile.Value.Checkpoints.Select(checkpoint => ToResponse(checkpoint, materials))]);
    }

    private static ScentCheckpointResponse ToResponse(
        ScentCheckpoint checkpoint,
        Dictionary<MaterialId, Material> materials) =>
        new(
            checkpoint.Elapsed.TotalMinutes,
            checkpoint.TotalIntensity,
            checkpoint.DominantFacet?.ToString(),
            Facets.All.ToDictionary(facet => facet.ToString(), facet => checkpoint.Facets[facet]),
            [.. checkpoint.TopContributors.Select(contributor => new ContributorResponse(
                contributor.MaterialId.Value,
                materials[contributor.MaterialId].Name,
                contributor.Share))]);
}
