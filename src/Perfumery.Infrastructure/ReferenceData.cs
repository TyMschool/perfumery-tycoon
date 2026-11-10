using System.Text.Json;
using Perfumery.SharedKernel;

namespace Perfumery.Infrastructure;

/// <summary>
/// Loading of curated JSON that ships with the game. Anything invalid in it is a bug,
/// so it throws at load instead of returning an error.
/// </summary>
internal static class ReferenceData
{
    public static T[] LoadArray<T>(string resourceName) =>
        LoadArray<T>(EmbeddedResources.Open(resourceName), resourceName);

    public static T[] LoadArray<T>(Stream json, string sourceName)
    {
        using (json)
        {
            return JsonSerializer.Deserialize<T[]>(json, EmbeddedResources.JsonOptions)
                ?? throw new InvalidOperationException($"{sourceName} is empty.");
        }
    }

    public static T Require<T>(Result<T> result, string sourceName, string id) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"{sourceName}: '{id}' is invalid: {result.Error.Description}");

    public static Dictionary<TKey, TValue> Index<TKey, TValue>(
        IEnumerable<TValue> items,
        Func<TValue, TKey> key,
        string sourceName)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, TValue>();
        foreach (var item in items)
        {
            if (!index.TryAdd(key(item), item))
            {
                throw new InvalidOperationException($"{sourceName} lists '{key(item)}' twice.");
            }
        }

        return index;
    }
}
