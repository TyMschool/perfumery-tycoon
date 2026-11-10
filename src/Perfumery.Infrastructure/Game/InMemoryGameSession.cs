using System.Diagnostics.CodeAnalysis;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Infrastructure.Game;

/// <summary>
/// The running game, held in memory for the life of the process. Domain events are
/// cleared when the game is snapshotted for saving, where a later phase will dispatch them.
/// </summary>
internal sealed class InMemoryGameSession : IGameSession
{
    private readonly Dictionary<FormulaId, Formula> _formulas = [];
    private readonly Dictionary<ExtractionRunId, ExtractionRun> _extractionRuns = [];
    private readonly Dictionary<ConcentrateBatchId, ConcentrateBatch> _batches = [];
    private readonly Dictionary<ProductRunId, ProductRun> _productRuns = [];

    [MemberNotNullWhen(true, nameof(Atelier), nameof(Inventory))]
    public bool IsStarted => Atelier is not null && Inventory is not null;

    public Atelier? Atelier { get; private set; }

    public Inventory? Inventory { get; private set; }

    public IReadOnlyCollection<Formula> Formulas => _formulas.Values;

    public IReadOnlyCollection<ExtractionRun> ExtractionRuns => _extractionRuns.Values;

    public IReadOnlyCollection<ConcentrateBatch> ConcentrateBatches => _batches.Values;

    public IReadOnlyCollection<ProductRun> ProductRuns => _productRuns.Values;

    public Formula? Find(FormulaId id) => _formulas.GetValueOrDefault(id);

    public ExtractionRun? Find(ExtractionRunId id) => _extractionRuns.GetValueOrDefault(id);

    public ConcentrateBatch? Find(ConcentrateBatchId id) => _batches.GetValueOrDefault(id);

    public ProductRun? Find(ProductRunId id) => _productRuns.GetValueOrDefault(id);

    public void Add(Formula formula) => _formulas.Add(formula.Id, formula);

    public void Add(ExtractionRun run) => _extractionRuns.Add(run.Id, run);

    public void Add(ConcentrateBatch batch) => _batches.Add(batch.Id, batch);

    public void Add(ProductRun run) => _productRuns.Add(run.Id, run);

    public void Start(Atelier atelier, Inventory inventory)
    {
        Atelier = atelier;
        Inventory = inventory;
        _extractionRuns.Clear();
        _batches.Clear();
        _productRuns.Clear();
    }

    public void Load(GameState state)
    {
        Atelier = state.Atelier;
        Inventory = state.Inventory;
        Replace(_formulas, state.Formulas, formula => formula.Id);
        Replace(_extractionRuns, state.ExtractionRuns, run => run.Id);
        Replace(_batches, state.ConcentrateBatches, batch => batch.Id);
        Replace(_productRuns, state.ProductRuns, run => run.Id);
    }

    public void PruneFinished()
    {
        Remove(_extractionRuns, run => run.Status == ExtractionStatus.Collected);
        Remove(_productRuns, run => run.Status == ProductRunStatus.Bottled);

        var referenced = _productRuns.Values.Select(run => run.BatchId).ToHashSet();
        Remove(_batches, batch => batch.IsExhausted && !referenced.Contains(batch.Id));
    }

    public GameSnapshot ToSnapshot()
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException("There is no running game to snapshot.");
        }

        IEnumerable<IHasDomainEvents> aggregates =
        [
            Atelier, Inventory, .. _formulas.Values, .. _extractionRuns.Values, .. _batches.Values, .. _productRuns.Values,
        ];
        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        return new GameSnapshot(
            Atelier.ToSnapshot(),
            Inventory.ToSnapshot(),
            [.. _formulas.Values.Select(formula => formula.ToSnapshot())],
            [.. _extractionRuns.Values.Select(run => run.ToSnapshot())],
            [.. _batches.Values.Select(batch => batch.ToSnapshot())],
            [.. _productRuns.Values.Select(run => run.ToSnapshot())]);
    }

    private static void Replace<TKey, TValue>(Dictionary<TKey, TValue> target, IEnumerable<TValue> items, Func<TValue, TKey> key)
        where TKey : notnull
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(key(item), item);
        }
    }

    private static void Remove<TKey, TValue>(Dictionary<TKey, TValue> target, Func<TValue, bool> finished)
        where TKey : notnull
    {
        foreach (var key in target.Where(pair => finished(pair.Value)).Select(pair => pair.Key).ToArray())
        {
            target.Remove(key);
        }
    }
}

/// <summary>The only place the system clock is read.</summary>
internal sealed class SystemGameClock(TimeProvider timeProvider) : IGameClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
