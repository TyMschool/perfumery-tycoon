namespace Perfumery.Application.Formulas.EvaluateFormula;

/// <param name="FormulaId">The evaluated formula.</param>
/// <param name="FormulaName">Its name.</param>
/// <param name="Revision">The revision that was evaluated.</param>
/// <param name="CostPerKgOfConcentrate">Material cost of one kilogram of concentrate.</param>
/// <param name="SolventFraction">Carrier share of the concentrate, 0 to 1.</param>
/// <param name="Checkpoints">The blend on a blotter at each checkpoint, earliest first.</param>
public sealed record ScentProfileResponse(
    Guid FormulaId,
    string FormulaName,
    int Revision,
    decimal CostPerKgOfConcentrate,
    decimal SolventFraction,
    IReadOnlyList<ScentCheckpointResponse> Checkpoints);

/// <param name="ElapsedMinutes">Minutes since the blotter was dipped.</param>
/// <param name="TotalIntensity">Total perceived intensity.</param>
/// <param name="DominantFacet">Name of the strongest facet; null if nothing is left.</param>
/// <param name="Facets">Facet shape keyed by facet name, summing to 1.</param>
/// <param name="TopContributors">The materials contributing most, strongest first.</param>
public sealed record ScentCheckpointResponse(
    double ElapsedMinutes,
    double TotalIntensity,
    string? DominantFacet,
    IReadOnlyDictionary<string, double> Facets,
    IReadOnlyList<ContributorResponse> TopContributors);

/// <param name="MaterialId">Catalog slug.</param>
/// <param name="MaterialName">Display name.</param>
/// <param name="Share">Share of the checkpoint's total perceived intensity, 0 to 1.</param>
public sealed record ContributorResponse(string MaterialId, string MaterialName, double Share);
