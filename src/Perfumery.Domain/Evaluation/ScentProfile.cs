using Perfumery.Domain.Common;
using Perfumery.Domain.Materials;

namespace Perfumery.Domain.Evaluation;

/// <summary>How a formula smells on a blotter over time, plus what its concentrate costs.</summary>
public sealed record ScentProfile(IReadOnlyList<ScentCheckpoint> Checkpoints, Money CostPerKgOfConcentrate);

/// <summary>The blend at one moment after application.</summary>
/// <param name="Elapsed">Time since the blotter was dipped.</param>
/// <param name="TotalIntensity">Sum of the perceived intensities of all materials.</param>
/// <param name="Facets">Facet shape of the blend, normalized to sum to 1 (all zero if nothing is left).</param>
/// <param name="TopContributors">The materials contributing most, strongest first.</param>
public sealed record ScentCheckpoint(
    TimeSpan Elapsed,
    double TotalIntensity,
    Facets Facets,
    IReadOnlyList<MaterialContribution> TopContributors)
{
    public Facet? DominantFacet => Facets.Dominant;
}

/// <param name="MaterialId">The contributing material.</param>
/// <param name="PerceivedIntensity">Its perceived intensity at the checkpoint.</param>
/// <param name="Share">Its share of the checkpoint's total perceived intensity, 0 to 1.</param>
public sealed record MaterialContribution(MaterialId MaterialId, double PerceivedIntensity, double Share);
