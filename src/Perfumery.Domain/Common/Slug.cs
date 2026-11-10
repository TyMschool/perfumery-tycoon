using System.Text.RegularExpressions;

namespace Perfumery.Domain.Common;

/// <summary>Lowercase words joined by hyphens, the shape of every reference-data ID.</summary>
public static partial class Slug
{
    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
