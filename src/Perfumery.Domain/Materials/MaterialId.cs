using System.Text.RegularExpressions;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Materials;

/// <summary>Stable slug identifying a material, e.g. <c>bergamot-calabria</c>.</summary>
public sealed partial record MaterialId
{
    private MaterialId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<MaterialId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MaterialErrors.IdRequired;
        }

        return SlugPattern().IsMatch(value) ? new MaterialId(value) : MaterialErrors.IdMalformed;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
