using Perfumery.Domain.Common;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

/// <param name="MaterialId">The material.</param>
/// <param name="NeatFraction">Its share of the concentrate as neat material, above 0 and at most 1.</param>
public sealed record CompositionLine(MaterialId MaterialId, decimal NeatFraction);

/// <summary>
/// A frozen formula: how much of each neat material one unit of concentrate holds, the
/// rest being carrier. Batches keep the composition they were made from, so revising
/// the formula later changes nothing already produced.
/// </summary>
public sealed class Composition : IEquatable<Composition>
{
    private readonly CompositionLine[] _lines;

    private Composition(CompositionLine[] lines, decimal solventFraction)
    {
        _lines = lines;
        SolventFraction = solventFraction;
    }

    public IReadOnlyList<CompositionLine> Lines => _lines;

    /// <summary>The carrier share: 1 minus the sum of the neat fractions.</summary>
    public decimal SolventFraction { get; }

    public static Result<Composition> Create(IEnumerable<CompositionLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var all = lines.ToArray();
        if (all.Length == 0)
        {
            return FormulaErrors.EmptyFormula;
        }

        if (all.Any(line => line.NeatFraction is <= 0m or > 1m))
        {
            return CompositionErrors.FractionOutOfRange;
        }

        if (all.Select(line => line.MaterialId).Distinct().Count() != all.Length)
        {
            return CompositionErrors.DuplicateMaterial;
        }

        var neat = all.Sum(line => line.NeatFraction);

        // Fractions come from divisions, so allow the last decimal places to overshoot.
        if (neat > 1.0000000001m)
        {
            return CompositionErrors.FractionsExceedWhole;
        }

        return new Composition(all, Math.Max(0m, 1m - neat));
    }

    /// <summary>
    /// Cost of one kilogram at the given prices: sum of neat fraction × material cost,
    /// plus the carrier share × the carrier's cost.
    /// </summary>
    public Result<Money> CostPerKg(IReadOnlyDictionary<MaterialId, Material> materials, Money carrierCostPerKg)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(carrierCostPerKg);

        var total = carrierCostPerKg.Times(SolventFraction);
        foreach (var line in _lines)
        {
            if (!materials.TryGetValue(line.MaterialId, out var material))
            {
                return MaterialErrors.NotFound(line.MaterialId);
            }

            total += material.CostPerKg.Times(line.NeatFraction);
        }

        return total;
    }

    public bool Equals(Composition? other) => other is not null && _lines.AsSpan().SequenceEqual(other._lines);

    public override bool Equals(object? obj) => Equals(obj as Composition);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var line in _lines)
        {
            hash.Add(line);
        }

        return hash.ToHashCode();
    }
}

public static class CompositionErrors
{
    public static readonly Error FractionOutOfRange =
        Error.Validation("Composition.FractionOutOfRange", "Each neat fraction must be above 0 and at most 1.");

    public static readonly Error DuplicateMaterial =
        Error.Validation("Composition.DuplicateMaterial", "A material can appear only once in a composition.");

    public static readonly Error FractionsExceedWhole =
        Error.Validation("Composition.FractionsExceedWhole", "The neat fractions cannot add up to more than 1.");
}
