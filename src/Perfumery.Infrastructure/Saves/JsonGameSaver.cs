using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Infrastructure.Saves;

/// <summary>
/// The whole game as one versioned JSON document. Reference data is never saved; the
/// document holds IDs only. Older documents are migrated on load, one version at a time.
/// </summary>
internal sealed class JsonGameSaver(ISaveStore store) : IGameSaver
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Migration n turns a version n document into version n + 1.</summary>
    private static readonly Func<JsonObject, JsonObject>[] Migrations = [MigrateV0ToV1];

    public Task SaveAsync(GameSnapshot snapshot, DateTimeOffset savedAt, CancellationToken cancellationToken)
    {
        var document = new SaveDocument(
            CurrentSchemaVersion,
            savedAt,
            snapshot.Atelier,
            snapshot.Inventory,
            snapshot.Formulas,
            snapshot.ExtractionRuns,
            snapshot.ConcentrateBatches,
            snapshot.ProductRuns);

        return store.WriteAsync(JsonSerializer.Serialize(document, Options), cancellationToken);
    }

    public async Task<Result<GameSnapshot?>> LoadAsync(CancellationToken cancellationToken)
    {
        var text = await store.ReadAsync(cancellationToken);
        if (text is null)
        {
            return Result.Success<GameSnapshot?>(null);
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject root)
            {
                return SaveErrors.Unreadable;
            }

            var version = root["schemaVersion"]?.GetValue<int>() ?? 0;
            if (version > CurrentSchemaVersion)
            {
                return SaveErrors.NewerVersion(version, CurrentSchemaVersion);
            }

            if (version < 0)
            {
                return SaveErrors.Unreadable;
            }

            for (; version < CurrentSchemaVersion; version++)
            {
                root = Migrations[version](root);
            }

            var document = root.Deserialize<SaveDocument>(Options);
            if (document is null)
            {
                return SaveErrors.Unreadable;
            }

            return new GameSnapshot(
                document.Atelier,
                document.Inventory,
                document.Formulas,
                document.ExtractionRuns,
                document.ConcentrateBatches,
                document.ProductRuns);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return SaveErrors.Unreadable;
        }
    }

    public async Task<bool> ExistsAsync(CancellationToken cancellationToken) =>
        await store.ReadAsync(cancellationToken) is not null;

    public Task SetAsideAsync(CancellationToken cancellationToken) => store.SetAsideAsync(cancellationToken);

    /// <summary>
    /// Version 0, from before the first release, had no schema version, kept no
    /// carried-forward ledger balance and had no shelf.
    /// </summary>
    private static JsonObject MigrateV0ToV1(JsonObject root)
    {
        if (root["atelier"] is JsonObject atelier && !atelier.ContainsKey("carriedForward"))
        {
            atelier["carriedForward"] = 0;
        }

        if (root["inventory"] is JsonObject inventory && !inventory.ContainsKey("shelf"))
        {
            inventory["shelf"] = new JsonArray();
        }

        root["schemaVersion"] = 1;
        return root;
    }

    private sealed record SaveDocument(
        int SchemaVersion,
        DateTimeOffset SavedAt,
        AtelierSnapshot Atelier,
        InventorySnapshot Inventory,
        IReadOnlyList<FormulaSnapshot> Formulas,
        IReadOnlyList<ExtractionRunSnapshot> ExtractionRuns,
        IReadOnlyList<ConcentrateBatchSnapshot> ConcentrateBatches,
        IReadOnlyList<ProductRunSnapshot> ProductRuns);
}
