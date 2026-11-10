using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Perfumery.Application;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Formulas.AddFormulaLine;
using Perfumery.Application.Formulas.CreateFormula;
using Perfumery.Application.Game.LoadGame;
using Perfumery.Application.Game.StartNewGame;
using Perfumery.Application.Production.BottleProductRun;
using Perfumery.Application.Production.CollectExtraction;
using Perfumery.Application.Production.CompoundConcentrate;
using Perfumery.Application.Production.GetProduction;
using Perfumery.Application.Production.StartExtraction;
using Perfumery.Application.Production.StartProductRun;
using Perfumery.Application.Stock.GetInventory;
using Perfumery.Application.Stock.GetShelf;
using Perfumery.Application.Stock.PurchaseSupplies;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.Infrastructure.Saves;
using Perfumery.SharedKernel;

namespace Perfumery.Infrastructure.UnitTests;

/// <summary>The whole stack against a real save file in a temporary directory, on a fake clock.</summary>
public sealed class SaveTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("perfumery-tests-");
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SavePath => Path.Combine(_directory.FullName, "save.json");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task A_tincture_started_before_closing_is_ready_three_days_later()
    {
        Guid runId;
        await using (var game = NewSession())
        {
            await game.Do<StartNewGameCommand, Result>(new());
            await game.Buy("supply:benzoin-resin", 1m);
            await game.Buy("supply:spirit-of-wine", 5m);
            runId = (await game.Do<StartExtractionCommand, Result<ExtractionRunId>>(new("tincture-benzoin", 1000m))).Value.Value;
        }

        _time.Advance(TimeSpan.FromDays(3));

        await using (var game = NewSession())
        {
            (await game.Do<LoadGameCommand, Result<LoadGameOutcome>>(new())).Value.ShouldBe(LoadGameOutcome.Loaded);

            var run = (await game.Ask<GetProductionQuery, Result<ProductionResponse>>(new())).Value.ExtractionRuns.ShouldHaveSingleItem();
            run.Id.ShouldBe(runId);
            run.Timing.IsComplete.ShouldBeTrue();
            run.Timing.RemainingSeconds.ShouldBe(0d);

            (await game.Do<CollectExtractionCommand, Result>(new(runId))).IsSuccess.ShouldBeTrue();
            var tincture = (await game.Ask<GetInventoryQuery, Result<IReadOnlyList<StockLineResponse>>>(new())).Value.ShouldHaveSingleItem();
            tincture.Item.ShouldBe("material:benzoin-tincture-siam");
            tincture.Quantity.ShouldBe(5m);
            // 30 francs of resin and 15 of spirit over 5 kg.
            tincture.AverageUnitCost.ShouldBe(9m);
        }
    }

    [Fact]
    public async Task Save_then_load_gives_the_same_game_with_jobs_at_every_stage()
    {
        GameSnapshot saved;
        await using (var game = NewSession())
        {
            await PlayToEveryStage(game);
            saved = game.Session.ToSnapshot();
        }

        await using (var game = NewSession())
        {
            (await game.Do<LoadGameCommand, Result<LoadGameOutcome>>(new())).Value.ShouldBe(LoadGameOutcome.Loaded);

            var loaded = game.Session.ToSnapshot();
            Json(loaded).ShouldBe(Json(saved));
            loaded.ExtractionRuns.Count.ShouldBe(1);
            loaded.ConcentrateBatches.Count.ShouldBe(1);
            loaded.ProductRuns.Count.ShouldBe(1);
            loaded.Inventory.Shelf.Count.ShouldBe(1);
            loaded.Formulas.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Finished_jobs_are_dropped_from_the_save_once_their_results_have_moved_on()
    {
        await using var game = NewSession();
        var (formulaId, batchId) = await PlayToEveryStage(game);
        var macerating = game.Session.ProductRuns.Single();
        _time.Advance(TimeSpan.FromMinutes(30));

        // Bottle the last run of the exhausted batch, and collect the extraction.
        (await game.Do<BottleProductRunCommand, Result>(new(macerating.Id.Value))).IsSuccess.ShouldBeTrue();
        (await game.Do<CollectExtractionCommand, Result>(new(game.Session.ExtractionRuns.Single().Id.Value))).IsSuccess.ShouldBeTrue();

        game.Session.ProductRuns.ShouldBeEmpty();
        game.Session.ExtractionRuns.ShouldBeEmpty();
        game.Session.Find(new ConcentrateBatchId(batchId)).ShouldBeNull();
        game.Session.Find(new FormulaId(formulaId)).ShouldNotBeNull();
        (await game.Ask<GetShelfQuery, Result<IReadOnlyList<ShelfLotResponse>>>(new())).Value.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_part_used_batch_and_a_batch_a_run_still_needs_are_kept()
    {
        await using var game = NewSession();
        var (_, batchId) = await PlayToEveryStage(game);

        // Exhausted, but the macerating run still refers to it.
        game.Session.Find(new ConcentrateBatchId(batchId)).ShouldNotBeNull().IsExhausted.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"schemaVersion":99,"atelier":{}}""", "Save.NewerVersion")]
    [InlineData("""{"schemaVersion":1,"savedAt":"2026-11-10T09:00:00Z","atelier":{"id":"3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90","cash":""", "Save.Unreadable")]
    [InlineData("not json at all", "Save.Unreadable")]
    [InlineData("[]", "Save.Unreadable")]
    [InlineData("""{"schemaVersion":1}""", "Save.Unreadable")]
    [InlineData("""{"schemaVersion":1,"savedAt":"2026-11-10T09:00:00Z","atelier":null,"inventory":null,"formulas":[],"extractionRuns":[],"concentrateBatches":[],"productRuns":[]}""", "Save.Unreadable")]
    public async Task A_save_that_cannot_be_read_fails_with_a_typed_error_and_is_left_untouched(string content, string code)
    {
        await File.WriteAllTextAsync(SavePath, content, Ct);

        await using (var game = NewSession())
        {
            var result = await game.Do<LoadGameCommand, Result<LoadGameOutcome>>(new());

            result.Error.Code.ShouldBe(code);
            game.Session.IsStarted.ShouldBeFalse();
        }

        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldBe(content);
    }

    [Fact]
    public async Task A_save_naming_an_unknown_material_fails_and_is_left_untouched()
    {
        await using (var game = NewSession())
        {
            await game.Do<StartNewGameCommand, Result>(new());
            await game.Buy("material:bergamot-calabria", 1m);
        }

        var content = (await File.ReadAllTextAsync(SavePath, Ct)).Replace("material:bergamot-calabria", "material:oakmoss-yugoslavia");
        await File.WriteAllTextAsync(SavePath, content, Ct);

        await using (var game = NewSession())
        {
            var result = await game.Do<LoadGameCommand, Result<LoadGameOutcome>>(new());

            result.Error.Code.ShouldBe("Save.UnknownReference");
            result.Error.Description.ShouldContain("oakmoss-yugoslavia");
        }

        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldBe(content);
    }

    [Fact]
    public async Task A_save_whose_cash_does_not_match_its_ledger_fails()
    {
        await using (var game = NewSession())
        {
            await game.Do<StartNewGameCommand, Result>(new());
        }

        var content = (await File.ReadAllTextAsync(SavePath, Ct)).Replace("\"cash\": 5000", "\"cash\": 999999");
        await File.WriteAllTextAsync(SavePath, content, Ct);

        await using var reopened = NewSession();
        (await reopened.Do<LoadGameCommand, Result<LoadGameOutcome>>(new())).Error.Code.ShouldBe("Save.Invalid");
    }

    [Fact]
    public async Task A_version_0_save_migrates_to_the_current_version()
    {
        // Version 0 had no schema version, no carried-forward ledger balance and no shelf.
        const string versionZero = """
            {
              "savedAt": "2026-11-01T09:00:00Z",
              "atelier": {
                "id": "3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90",
                "cash": 4970,
                "lastSeen": "2026-11-01T09:00:00Z",
                "ledger": [
                  { "at": "2026-11-01T08:00:00Z", "amount": 5000, "category": "startingCapital", "reference": "Starting capital" },
                  { "at": "2026-11-01T09:00:00Z", "amount": -30, "category": "suppliesPurchased", "reference": "spirit" }
                ]
              },
              "inventory": {
                "id": "7d4e5f6a-5b7d-4e8a-9c21-3f2a9c1e8b90",
                "lines": [ { "item": "supply:spirit-of-wine", "quantity": 10, "totalCost": 30 } ]
              },
              "formulas": [],
              "extractionRuns": [],
              "concentrateBatches": [],
              "productRuns": []
            }
            """;
        await File.WriteAllTextAsync(SavePath, versionZero, Ct);

        await using var game = NewSession();
        (await game.Do<LoadGameCommand, Result<LoadGameOutcome>>(new())).Value.ShouldBe(LoadGameOutcome.Loaded);

        game.Session.Atelier!.Cash.Amount.ShouldBe(4970m);
        game.Session.Atelier.Ledger.Count.ShouldBe(2);
        game.Session.Inventory!.Shelf.ShouldBeEmpty();

        // The next save is written at the current version.
        await game.Buy("supply:spirit-of-wine", 1m);
        await game.DisposeAsync();
        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldContain($"\"schemaVersion\": {JsonGameSaver.CurrentSchemaVersion}");
    }

    [Fact]
    public async Task Starting_a_new_game_over_an_unloaded_save_copies_it_aside_first()
    {
        const string damaged = "not json at all";
        await File.WriteAllTextAsync(SavePath, damaged, Ct);

        await using var game = NewSession();
        (await game.Do<LoadGameCommand, Result<LoadGameOutcome>>(new())).IsFailure.ShouldBeTrue();
        (await game.Do<StartNewGameCommand, Result>(new())).IsSuccess.ShouldBeTrue();

        (await File.ReadAllTextAsync(Path.Combine(_directory.FullName, "save.corrupt.json"), Ct)).ShouldBe(damaged);
        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldContain("\"schemaVersion\"");
    }

    [Fact]
    public async Task With_no_save_file_loading_reports_that_there_is_none()
    {
        await using var game = NewSession();

        (await game.Do<LoadGameCommand, Result<LoadGameOutcome>>(new())).Value.ShouldBe(LoadGameOutcome.NoSave);
        File.Exists(SavePath).ShouldBeFalse();
    }

    // ---- the file store itself ------------------------------------------------------

    [Fact]
    public async Task Writes_within_the_interval_are_coalesced_into_one_and_flushed_when_due()
    {
        await using var store = new FileSaveStore(new SaveOptions { Path = SavePath }, _time);

        await store.WriteAsync("one", Ct);
        store.FileWrites.ShouldBe(1);

        // A slider drag: many saves in well under a second.
        for (var i = 0; i < 50; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(10));
            await store.WriteAsync($"drag {i}", Ct);
        }

        store.FileWrites.ShouldBe(1);
        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldBe("one");
        (await store.ReadAsync(Ct)).ShouldBe("drag 49");

        _time.Advance(TimeSpan.FromSeconds(1));

        store.FileWrites.ShouldBe(2);
        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldBe("drag 49");
    }

    [Fact]
    public async Task A_pending_write_is_flushed_on_shutdown()
    {
        var store = new FileSaveStore(new SaveOptions { Path = SavePath }, _time);
        await store.WriteAsync("one", Ct);
        await store.WriteAsync("two", Ct);
        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldBe("one");

        await store.DisposeAsync();

        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldBe("two");
    }

    [Fact]
    public async Task An_interrupted_write_leaves_the_previous_save_readable()
    {
        await using var store = new FileSaveStore(new SaveOptions { Path = SavePath, MinWriteInterval = TimeSpan.Zero }, _time);
        await store.WriteAsync("good save", Ct);

        // A crash mid-write leaves a half-written temporary file and never touches the save.
        await File.WriteAllTextAsync(SavePath + ".tmp", "{ \"schemaVer", Ct);

        (await store.ReadAsync(Ct)).ShouldBe("good save");
        await store.WriteAsync("next save", Ct);
        (await File.ReadAllTextAsync(SavePath, Ct)).ShouldBe("next save");
        File.Exists(SavePath + ".tmp").ShouldBeFalse();
    }

    // ---- helpers --------------------------------------------------------------------

    /// <summary>
    /// Leaves a running extraction, an exhausted batch, one macerating run and one bottled
    /// lot on the shelf. Returns the formula and batch IDs.
    /// </summary>
    private async Task<(Guid FormulaId, Guid BatchId)> PlayToEveryStage(GameHost game)
    {
        await game.Do<StartNewGameCommand, Result>(new());
        var formulaId = (await game.Do<CreateFormulaCommand, Result<FormulaId>>(new("Rose water"))).Value.Value;
        (await game.Do<AddFormulaLineCommand, Result>(new(formulaId, "bergamot-calabria", 10m))).IsSuccess.ShouldBeTrue();
        (await game.Do<AddFormulaLineCommand, Result>(new(formulaId, "rose-otto-kazanlak", 10m))).IsSuccess.ShouldBeTrue();

        await game.Buy("material:bergamot-calabria", 0.15m);
        await game.Buy("material:rose-otto-kazanlak", 0.015m);
        await game.Buy("supply:spirit-of-wine", 12m);
        await game.Buy("supply:flacon-cologne-200g", 40m);
        await game.Buy("supply:lavender-flowering-tops", 50m);

        var batchId = (await game.Do<CompoundConcentrateCommand, Result<ConcentrateBatchId>>(new(formulaId, 300m))).Value.Value;
        _time.Advance(TimeSpan.FromMinutes(15));
        var first = (await game.Do<StartProductRunCommand, Result<ProductRunId>>(new(batchId, "eau-de-cologne", 150m))).Value.Value;
        _time.Advance(TimeSpan.FromMinutes(30));
        (await game.Do<BottleProductRunCommand, Result>(new(first))).IsSuccess.ShouldBeTrue();

        (await game.Do<StartProductRunCommand, Result<ProductRunId>>(new(batchId, "eau-de-cologne", 150m, 5m))).IsSuccess.ShouldBeTrue();
        (await game.Do<StartExtractionCommand, Result<ExtractionRunId>>(new("distil-lavender", 50_000m))).IsSuccess.ShouldBeTrue();
        return (formulaId, batchId);
    }

    private static string Json(GameSnapshot snapshot) => System.Text.Json.JsonSerializer.Serialize(snapshot);

    private GameHost NewSession() => new(SavePath, _time);

    /// <summary>One run of the game: its own container, session and save store over the shared file.</summary>
    private sealed class GameHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private bool _disposed;

        public GameHost(string savePath, TimeProvider time)
        {
            _provider = new ServiceCollection()
                .AddLogging()
                .AddSingleton(time)
                .AddApplication()
                .AddInfrastructure(options => options.Path = savePath)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            _scope = _provider.CreateAsyncScope();
        }

        public IGameSession Session => _provider.GetRequiredService<IGameSession>();

        public Task<TResponse> Do<TCommand, TResponse>(TCommand command)
            where TCommand : ICommand<TResponse>
            where TResponse : Result =>
            _scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().Handle(command, Ct);

        public Task<TResponse> Ask<TQuery, TResponse>(TQuery query)
            where TQuery : IQuery<TResponse>
            where TResponse : Result =>
            _scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>().Handle(query, Ct);

        public async Task Buy(string item, decimal quantity)
        {
            var result = await Do<PurchaseSuppliesCommand, Result>(new(item, quantity));
            result.IsSuccess.ShouldBeTrue(result.Error.Description);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }
}
