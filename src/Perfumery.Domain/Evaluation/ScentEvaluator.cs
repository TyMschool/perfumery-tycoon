using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Evaluation;

/// <summary>
/// Pure domain service: turns a composition and its materials into a <see cref="ScentProfile"/>.
/// Known simplifications: no masking, synergy or anosmia; facets add linearly.
/// </summary>
public sealed class ScentEvaluator
{
    private readonly EvaluationSettings _settings;

    public ScentEvaluator(EvaluationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <param name="composition">The frozen formula to evaluate.</param>
    /// <param name="materials">Every material the composition names.</param>
    /// <param name="carrierCostPerKg">What the carrier costs, for the cost per kg of concentrate.</param>
    public Result<ScentProfile> Evaluate(
        Composition composition,
        IReadOnlyDictionary<MaterialId, Material> materials,
        Money carrierCostPerKg)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(materials);

        var cost = composition.CostPerKg(materials, carrierCostPerKg);
        if (cost.IsFailure)
        {
            return cost.Error;
        }

        // Cost succeeding means every line's material is present.
        var inputs = composition.Lines
            .Select(line => new LineInput(materials[line.MaterialId], (double)line.NeatFraction))
            .ToArray();

        var checkpoints = _settings.Checkpoints.Select(elapsed => EvaluateAt(elapsed, inputs)).ToArray();

        return new ScentProfile(checkpoints, cost.Value);
    }

    private ScentCheckpoint EvaluateAt(TimeSpan elapsed, LineInput[] inputs)
    {
        var facetTotals = new double[Facets.All.Count];
        var perceived = new double[inputs.Length];
        var total = 0d;

        for (var i = 0; i < inputs.Length; i++)
        {
            var (material, neatFraction) = inputs[i];
            var remaining = EvaporationModel.RemainingShare(material.Volatility, elapsed);
            var raw = EvaporationModel.RawIntensity(neatFraction, material.OdorStrength, remaining);
            perceived[i] = EvaporationModel.PerceivedIntensity(raw, _settings.PerceptionExponent);
            total += perceived[i];

            foreach (var facet in Facets.All)
            {
                facetTotals[(int)facet] += perceived[i] * material.Facets[facet];
            }
        }

        var contributors = inputs
            .Select((input, i) => new MaterialContribution(
                input.Material.Id,
                perceived[i],
                total > 0d ? perceived[i] / total : 0d))
            .OrderByDescending(contribution => contribution.PerceivedIntensity)
            .ThenBy(contribution => contribution.MaterialId.Value, StringComparer.Ordinal)
            .Take(_settings.TopContributorCount)
            .ToArray();

        return new ScentCheckpoint(elapsed, total, Normalize(facetTotals), contributors);
    }

    private static Facets Normalize(double[] facetTotals)
    {
        var norm = facetTotals.Sum();
        if (norm <= 0d)
        {
            return Facets.None;
        }

        var weights = Facets.All.Select(facet =>
            KeyValuePair.Create(facet, Math.Clamp(facetTotals[(int)facet] / norm, 0d, 1d)));

        return Facets.Create(weights).Value;
    }

    private readonly record struct LineInput(Material Material, double NeatFraction);
}
