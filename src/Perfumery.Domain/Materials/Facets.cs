using Perfumery.SharedKernel;

namespace Perfumery.Domain.Materials;

/// <summary>Odor character as a weight from 0 to 1 for each <see cref="Facet"/>.</summary>
public sealed class Facets : IEquatable<Facets>
{
    private static readonly Facet[] AllFacets = Enum.GetValues<Facet>();

    private readonly double[] _weights;

    private Facets(double[] weights)
    {
        _weights = weights;
    }

    public static IReadOnlyList<Facet> All => AllFacets;

    public static Facets None { get; } = new(new double[AllFacets.Length]);

    public double this[Facet facet] => _weights[(int)facet];

    public double Sum => _weights.Sum();

    /// <summary>The strongest facet, or null when every weight is zero. Ties go to the first facet in enum order.</summary>
    public Facet? Dominant
    {
        get
        {
            var best = 0;
            for (var i = 1; i < _weights.Length; i++)
            {
                if (_weights[i] > _weights[best])
                {
                    best = i;
                }
            }

            return _weights[best] > 0d ? AllFacets[best] : null;
        }
    }

    /// <summary>Builds a facet set from the given weights; facets left out are zero.</summary>
    public static Result<Facets> Create(IEnumerable<KeyValuePair<Facet, double>> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var values = new double[AllFacets.Length];
        foreach (var (facet, weight) in weights)
        {
            if (!Enum.IsDefined(facet))
            {
                return FacetErrors.UnknownFacet;
            }

            if (double.IsNaN(weight) || weight < 0d || weight > 1d)
            {
                return FacetErrors.WeightOutOfRange;
            }

            values[(int)facet] = weight;
        }

        return new Facets(values);
    }

    public IReadOnlyDictionary<Facet, double> ToDictionary() =>
        AllFacets.ToDictionary(facet => facet, facet => _weights[(int)facet]);

    public bool Equals(Facets? other) => other is not null && _weights.AsSpan().SequenceEqual(other._weights);

    public override bool Equals(object? obj) => Equals(obj as Facets);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var weight in _weights)
        {
            hash.Add(weight);
        }

        return hash.ToHashCode();
    }
}

public static class FacetErrors
{
    public static readonly Error WeightOutOfRange =
        Error.Validation("Facets.WeightOutOfRange", "Each facet weight must be between 0 and 1.");

    public static readonly Error UnknownFacet =
        Error.Validation("Facets.UnknownFacet", "The facet is not one of the known odor facets.");
}
