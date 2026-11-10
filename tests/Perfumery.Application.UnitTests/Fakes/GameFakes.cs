using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Time.Testing;
using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Application.UnitTests.Fakes;

internal sealed class FakeGameSession : IGameSession, IFormulaRepository
{
    private readonly List<Formula> _formulas = [];
    private readonly List<ExtractionRun> _extractionRuns = [];
    private readonly List<ConcentrateBatch> _batches = [];
    private readonly List<ProductRun> _productRuns = [];

    [MemberNotNullWhen(true, nameof(Atelier), nameof(Inventory))]
    public bool IsStarted => Atelier is not null && Inventory is not null;

    public Atelier? Atelier { get; private set; }

    public Inventory? Inventory { get; private set; }

    public int PruneCount { get; private set; }

    public IReadOnlyCollection<Formula> Formulas => _formulas;

    public IReadOnlyCollection<ExtractionRun> ExtractionRuns => _extractionRuns;

    public IReadOnlyCollection<ConcentrateBatch> ConcentrateBatches => _batches;

    public IReadOnlyCollection<ProductRun> ProductRuns => _productRuns;

    public Formula? Find(FormulaId id) => _formulas.Find(formula => formula.Id == id);

    public ExtractionRun? Find(ExtractionRunId id) => _extractionRuns.Find(run => run.Id == id);

    public ConcentrateBatch? Find(ConcentrateBatchId id) => _batches.Find(batch => batch.Id == id);

    public ProductRun? Find(ProductRunId id) => _productRuns.Find(run => run.Id == id);

    public void Add(Formula formula) => _formulas.Add(formula);

    public void Add(ExtractionRun run) => _extractionRuns.Add(run);

    public void Add(ConcentrateBatch batch) => _batches.Add(batch);

    public void Add(ProductRun run) => _productRuns.Add(run);

    public void Start(Atelier atelier, Inventory inventory)
    {
        Atelier = atelier;
        Inventory = inventory;
    }

    public void Load(GameState state)
    {
        Atelier = state.Atelier;
        Inventory = state.Inventory;
        _formulas.Clear();
        _formulas.AddRange(state.Formulas);
        _extractionRuns.Clear();
        _extractionRuns.AddRange(state.ExtractionRuns);
        _batches.Clear();
        _batches.AddRange(state.ConcentrateBatches);
        _productRuns.Clear();
        _productRuns.AddRange(state.ProductRuns);
    }

    public void PruneFinished() => PruneCount++;

    public GameSnapshot ToSnapshot() =>
        new(
            Atelier!.ToSnapshot(),
            Inventory!.ToSnapshot(),
            [.. _formulas.Select(formula => formula.ToSnapshot())],
            [.. _extractionRuns.Select(run => run.ToSnapshot())],
            [.. _batches.Select(batch => batch.ToSnapshot())],
            [.. _productRuns.Select(run => run.ToSnapshot())]);

    Task<Formula?> IFormulaRepository.GetByIdAsync(FormulaId id, CancellationToken cancellationToken) =>
        Task.FromResult(Find(id));

    Task IFormulaRepository.AddAsync(Formula formula, CancellationToken cancellationToken)
    {
        Add(formula);
        return Task.CompletedTask;
    }

    Task IFormulaRepository.SaveAsync(Formula formula, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeGameSaver : IGameSaver
{
    public List<GameSnapshot> Saves { get; } = [];

    /// <summary>What <see cref="LoadAsync"/> returns. Null means there is no save.</summary>
    public Result<GameSnapshot?> Stored { get; set; } = Result.Success<GameSnapshot?>(null);

    public bool WasSetAside { get; private set; }

    public Task SaveAsync(GameSnapshot snapshot, DateTimeOffset savedAt, CancellationToken cancellationToken)
    {
        Saves.Add(snapshot);
        Stored = Result.Success<GameSnapshot?>(snapshot);
        return Task.CompletedTask;
    }

    public Task<Result<GameSnapshot?>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Stored);

    public Task<bool> ExistsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Stored.IsFailure || Stored.Value is not null);

    public Task SetAsideAsync(CancellationToken cancellationToken)
    {
        WasSetAside = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeGameClock(FakeTimeProvider time) : IGameClock
{
    /// <summary>Set to make the system clock read something else, such as an earlier time.</summary>
    public DateTimeOffset? Override { get; set; }

    public DateTimeOffset UtcNow => Override ?? time.GetUtcNow();
}

internal sealed class FakeExtractionRecipeCatalog : IExtractionRecipeCatalog
{
    public const string DistilLavenderId = "distil-lavender";

    private readonly List<ExtractionRecipe> _recipes =
    [
        ExtractionRecipe.Create(
            ExtractionRecipeId.Create(DistilLavenderId).Value,
            MaterialId.Create(FakeMaterialCatalog.LavenderId).Value,
            ExtractionMethod.SteamDistillation,
            SupplyId.Create(FakeSupplyCatalog.LavenderTopsId).Value,
            0m,
            Yield.Create(0.010m).Value,
            TimeSpan.FromMinutes(20),
            EquipmentKind.Still).Value,
    ];

    public IReadOnlyList<ExtractionRecipe> GetAll() => _recipes;

    public ExtractionRecipe? Find(ExtractionRecipeId id) => _recipes.Find(recipe => recipe.Id == id);
}

internal sealed class FakeProductTypeCatalog : IProductTypeCatalog
{
    public const string CologneId = "eau-de-cologne";

    private readonly List<ProductType> _productTypes =
    [
        ProductType.Create(
            ProductTypeId.Create(CologneId).Value,
            "Eau de Cologne",
            Concentration.FromPercent(2m).Value,
            Concentration.FromPercent(5m).Value,
            Concentration.FromPercent(3m).Value,
            TimeSpan.FromMinutes(30),
            SupplyId.Create(FakeSupplyCatalog.FlaconId).Value,
            Mass.FromGrams(200m).Value).Value,
    ];

    public IReadOnlyList<ProductType> GetAll() => _productTypes;

    public ProductType? Find(ProductTypeId id) => _productTypes.Find(productType => productType.Id == id);
}

internal static class FakeSettings
{
    /// <summary>5,000 francs, one 100 kg still, two 10 kg jars, three 20 kg vessels, 2% filter loss.</summary>
    public static AtelierSettings Create() =>
        AtelierSettings.Create(
            Money.Create(5000m).Value,
            new Dictionary<EquipmentKind, EquipmentSpec>
            {
                [EquipmentKind.Still] = new(1, Mass.FromKilograms(100m).Value),
                [EquipmentKind.TinctureJar] = new(2, Mass.FromKilograms(10m).Value),
                [EquipmentKind.MacerationVessel] = new(3, Mass.FromKilograms(20m).Value),
            },
            0.02m,
            1m,
            TimeSpan.FromMinutes(15)).Value;
}
