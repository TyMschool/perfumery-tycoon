using System.Diagnostics.CodeAnalysis;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;

namespace Perfumery.Application.Abstractions.Game;

/// <summary>
/// The running game: every aggregate, held in memory. Formulas can exist before a game
/// is started; everything else needs an atelier.
/// </summary>
public interface IGameSession
{
    [MemberNotNullWhen(true, nameof(Atelier), nameof(Inventory))]
    bool IsStarted { get; }

    Atelier? Atelier { get; }

    Inventory? Inventory { get; }

    IReadOnlyCollection<Formula> Formulas { get; }

    IReadOnlyCollection<ExtractionRun> ExtractionRuns { get; }

    IReadOnlyCollection<ConcentrateBatch> ConcentrateBatches { get; }

    IReadOnlyCollection<ProductRun> ProductRuns { get; }

    Formula? Find(FormulaId id);

    ExtractionRun? Find(ExtractionRunId id);

    ConcentrateBatch? Find(ConcentrateBatchId id);

    ProductRun? Find(ProductRunId id);

    void Add(Formula formula);

    void Add(ExtractionRun run);

    void Add(ConcentrateBatch batch);

    void Add(ProductRun run);

    /// <summary>Opens a new game, keeping any formulas already composed.</summary>
    void Start(Atelier atelier, Inventory inventory);

    /// <summary>Replaces everything with a loaded game.</summary>
    void Load(GameState state);

    /// <summary>
    /// Forgets jobs whose results have moved on: collected extractions, bottled product
    /// runs, and exhausted concentrate batches no product run still refers to.
    /// </summary>
    void PruneFinished();

    /// <summary>The whole game as plain data. Only valid once started.</summary>
    GameSnapshot ToSnapshot();
}

/// <summary>A complete set of aggregates, as restored from a save.</summary>
public sealed record GameState(
    Atelier Atelier,
    Inventory Inventory,
    IReadOnlyList<Formula> Formulas,
    IReadOnlyList<ExtractionRun> ExtractionRuns,
    IReadOnlyList<ConcentrateBatch> ConcentrateBatches,
    IReadOnlyList<ProductRun> ProductRuns);

/// <summary>The whole game as plain domain snapshots, ready to be written to a save.</summary>
public sealed record GameSnapshot(
    AtelierSnapshot Atelier,
    InventorySnapshot Inventory,
    IReadOnlyList<FormulaSnapshot> Formulas,
    IReadOnlyList<ExtractionRunSnapshot> ExtractionRuns,
    IReadOnlyList<ConcentrateBatchSnapshot> ConcentrateBatches,
    IReadOnlyList<ProductRunSnapshot> ProductRuns);
