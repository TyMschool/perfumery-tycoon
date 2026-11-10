using FluentValidation;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Materials.GetMaterials;

/// <param name="Tier">Only materials of this note tier; null returns the whole catalog.</param>
public sealed record GetMaterialsQuery(NoteTier? Tier = null) : IQuery<Result<IReadOnlyList<MaterialResponse>>>;

/// <param name="Id">Catalog slug.</param>
/// <param name="Name">Display name.</param>
/// <param name="BotanicalSource">Plant and part the material comes from.</param>
/// <param name="Origin">Where it is grown or made.</param>
/// <param name="ExtractionMethod">How it is obtained.</param>
/// <param name="NoteTier">Top, Heart or Base.</param>
/// <param name="Facets">Facet weights (0 to 1) keyed by facet name.</param>
/// <param name="OdorStrength">Relative impact multiplier.</param>
/// <param name="HalfLifeHours">Evaporation half-life on a blotter.</param>
/// <param name="CostPerKg">Cost of one kilogram neat.</param>
/// <param name="StockDilutionPercent">Dilution the lab keeps it at; 100 is neat.</param>
public sealed record MaterialResponse(
    string Id,
    string Name,
    string BotanicalSource,
    string Origin,
    string ExtractionMethod,
    string NoteTier,
    IReadOnlyDictionary<string, double> Facets,
    double OdorStrength,
    double HalfLifeHours,
    decimal CostPerKg,
    decimal StockDilutionPercent);

internal sealed class GetMaterialsQueryValidator : AbstractValidator<GetMaterialsQuery>
{
    public GetMaterialsQueryValidator()
    {
        RuleFor(query => query.Tier).IsInEnum();
    }
}

internal sealed class GetMaterialsQueryHandler(IMaterialCatalog catalog)
    : IQueryHandler<GetMaterialsQuery, Result<IReadOnlyList<MaterialResponse>>>
{
    public Task<Result<IReadOnlyList<MaterialResponse>>> Handle(GetMaterialsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaterialResponse> materials =
        [
            .. catalog.GetAll()
                .Where(material => query.Tier is null || material.Tier == query.Tier)
                .Select(ToResponse),
        ];

        return Task.FromResult(Result.Success(materials));
    }

    private static MaterialResponse ToResponse(Material material) =>
        new(
            material.Id.Value,
            material.Name,
            material.BotanicalSource,
            material.Origin,
            material.ExtractionMethod.ToString(),
            material.Tier.ToString(),
            Facets.All.ToDictionary(facet => facet.ToString(), facet => material.Facets[facet]),
            material.OdorStrength,
            material.Volatility.HalfLifeHours,
            material.CostPerKg.Amount,
            material.StockDilution.Percent);
}
