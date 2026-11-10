using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perfumery.Infrastructure;

internal static class EmbeddedResources
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static Stream Open(string resourceName) =>
        typeof(EmbeddedResources).Assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing.");
}
