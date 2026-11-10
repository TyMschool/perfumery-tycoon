using Microsoft.Extensions.Time.Testing;
using Perfumery.Application.Abstractions.Behaviors;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Ateliers.GetAtelier;
using Perfumery.Application.Game;
using Perfumery.Application.Game.LoadGame;
using Perfumery.Application.Game.StartNewGame;
using Perfumery.Application.Production.BottleProductRun;
using Perfumery.Application.Production.CollectExtraction;
using Perfumery.Application.Production.CompoundConcentrate;
using Perfumery.Application.Production.GetProduction;
using Perfumery.Application.Production.QuoteProduction;
using Perfumery.Application.Production.StartExtraction;
using Perfumery.Application.Production.StartProductRun;
using Perfumery.Application.Stock.GetInventory;
using Perfumery.Application.Stock.GetShelf;
using Perfumery.Application.Stock.PurchaseSupplies;
using Perfumery.Application.UnitTests.Fakes;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;
using static Perfumery.Application.UnitTests.Fakes.FakeMaterialCatalog;
using static Perfumery.Application.UnitTests.Fakes.FakeSupplyCatalog;

namespace Perfumery.Application.UnitTests.Production;

public class GameSliceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly FakeGameSession _session = new();
    private readonly FakeGameSaver _saver = new();
    private readonly FakeMaterialCatalog _materials = WithLavender();
    private readonly FakeSupplyCatalog _supplies = new();
    private readonly FakeExtractionRecipeCatalog _recipes = new();
    private readonly FakeProductTypeCatalog _productTypes = new();
    private readonly AtelierSettings _settings = FakeSettings.Create();
    private readonly FakeGameClock _clock;
    private readonly StockItemDirectory _items;

    public GameSliceTests()
    {
        _clock = new FakeGameClock(_time);
        _items = new StockItemDirectory(_materials, _supplies);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- starting and loading -------------------------------------------------------

    [Fact]
    public async Task StartNewGame_opens_an_atelier_with_the_starting_capital()
    {
        (await StartNewGame()).IsSuccess.ShouldBeTrue();

        _session.IsStarted.ShouldBeTrue();
        _session.Atelier!.Cash.Amount.ShouldBe(5000m);
        _session.Inventory!.Lines.ShouldBeEmpty();
        _saver.WasSetAside.ShouldBeFalse();
        (await StartNewGame()).Error.ShouldBe(GameErrors.AlreadyStarted);
    }

    [Fact]
    public async Task StartNewGame_sets_an_unloaded_save_aside_instead_of_losing_it()
    {
        _saver.Stored = SaveErrors.Unreadable;

        (await StartNewGame()).IsSuccess.ShouldBeTrue();

        _saver.WasSetAside.ShouldBeTrue();
    }

    [Fact]
    public async Task Everything_but_formulas_needs_a_running_game()
    {
        (await Buy($"supply:{SpiritId}", 1m)).Error.ShouldBe(GameErrors.NotStarted);
        (await StartExtraction(50_000m)).Error.ShouldBe(GameErrors.NotStarted);
        (await Collect(Guid.NewGuid())).Error.ShouldBe(GameErrors.NotStarted);
        (await Compound(Guid.NewGuid(), 100m)).Error.ShouldBe(GameErrors.NotStarted);
        (await StartRun(Guid.NewGuid(), 100m)).Error.ShouldBe(GameErrors.NotStarted);
        (await Bottle(Guid.NewGuid())).Error.ShouldBe(GameErrors.NotStarted);
        (await GetAtelier()).Error.ShouldBe(GameErrors.NotStarted);
        (await GetInventory()).Error.ShouldBe(GameErrors.NotStarted);
        (await GetProduction()).Error.ShouldBe(GameErrors.NotStarted);
        (await GetShelf()).Error.ShouldBe(GameErrors.NotStarted);
        (await Quote(Guid.NewGuid(), 100m)).Error.ShouldBe(GameErrors.NotStarted);
    }

    [Fact]
    public async Task LoadGame_reports_when_there_is_no_save()
    {
        var result = await Load();

        result.Value.ShouldBe(LoadGameOutcome.NoSave);
        _session.IsStarted.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadGame_restores_a_saved_game_with_jobs_at_every_stage()
    {
        var formulaId = await PlayToMaceratingRun();
        var saved = _session.ToSnapshot();
        _saver.Stored = Result.Success<GameSnapshot?>(saved);
        var fresh = new FakeGameSession();

        var result = await new LoadGameCommandHandler(fresh, _saver, Restorer()).Handle(new LoadGameCommand(), Ct);

        result.Value.ShouldBe(LoadGameOutcome.Loaded);
        fresh.Atelier!.Cash.ShouldBe(_session.Atelier!.Cash);
        fresh.Find(new FormulaId(formulaId)).ShouldNotBeNull();
        fresh.ExtractionRuns.Count.ShouldBe(1);
        fresh.ConcentrateBatches.Count.ShouldBe(1);
        fresh.ProductRuns.Count.ShouldBe(1);
        Json(fresh.ToSnapshot()).ShouldBe(Json(saved));
    }

    [Fact]
    public async Task LoadGame_fails_with_a_typed_error_and_leaves_the_session_alone()
    {
        await StartNewGame();
        await Buy($"material:{LavenderId}", 1m);
        var saved = _session.ToSnapshot();

        async Task<Error> LoadWith(GameSnapshot snapshot)
        {
            var fresh = new FakeGameSession();
            _saver.Stored = Result.Success<GameSnapshot?>(snapshot);
            var result = await new LoadGameCommandHandler(fresh, _saver, Restorer()).Handle(new LoadGameCommand(), Ct);
            fresh.IsStarted.ShouldBeFalse();
            return result.Error;
        }

        var unknownItem = saved with
        {
            Inventory = saved.Inventory with { Lines = [new StockLineSnapshot("material:oakmoss-yugoslavia", 1m, 10m)] },
        };
        (await LoadWith(unknownItem)).Code.ShouldBe("Save.UnknownReference");

        var unknownFormulaMaterial = saved with
        {
            Formulas = [new FormulaSnapshot(Guid.NewGuid(), "Chypre", 2, [new FormulaLineSnapshot("oakmoss-yugoslavia", 1m, 1m)])],
        };
        (await LoadWith(unknownFormulaMaterial)).Code.ShouldBe("Save.UnknownReference");

        var brokenLedger = saved with { Atelier = saved.Atelier with { Cash = 1m } };
        (await LoadWith(brokenLedger)).Code.ShouldBe("Save.Invalid");

        _saver.Stored = SaveErrors.NewerVersion(9, 1);
        (await Load()).Error.Code.ShouldBe("Save.NewerVersion");
    }

    // ---- purchasing -----------------------------------------------------------------

    [Fact]
    public async Task PurchaseSupplies_buys_supplies_and_materials_at_catalog_price()
    {
        await StartNewGame();

        (await Buy($"supply:{SpiritId}", 10m)).IsSuccess.ShouldBeTrue();
        (await Buy($"material:{RoseOttoId}", 0.01m)).IsSuccess.ShouldBeTrue();
        (await Buy($"supply:{FlaconId}", 49m)).IsSuccess.ShouldBeTrue();

        // 10 × 3.00 + 0.01 × 2400 + 49 × 0.40.
        _session.Atelier!.Cash.Amount.ShouldBe(5000m - 30m - 24m - 19.6m);
        _session.Inventory!.QuantityOf(StockItemId.Parse($"material:{RoseOttoId}").Value).Value.ShouldBe(0.01m);
    }

    [Theory]
    [InlineData("supply:moon-dust", 1, "Stock.ItemNotFound")]
    [InlineData("material:oakmoss-yugoslavia", 1, "Stock.ItemNotFound")]
    [InlineData("spirit", 1, "Stock.ItemIdMalformed")]
    [InlineData("supply:spirit-of-wine", -1, "Stock.QuantityNegative")]
    [InlineData("supply:spirit-of-wine", 0, "Stock.QuantityZero")]
    [InlineData("supply:flacon-cologne-200g", 1.5, "Stock.QuantityNotWhole")]
    [InlineData("material:rose-otto-kazanlak", 3, "Atelier.InsufficientFunds")]
    public async Task PurchaseSupplies_rejects_bad_orders_and_changes_nothing(string item, double quantity, string code)
    {
        await StartNewGame();

        var result = await Buy(item, (decimal)quantity);

        result.Error.Code.ShouldBe(code);
        _session.Atelier!.Cash.Amount.ShouldBe(5000m);
        _session.Inventory!.Lines.ShouldBeEmpty();
    }

    // ---- extraction -----------------------------------------------------------------

    [Fact]
    public async Task An_extraction_runs_on_the_clock_and_is_collected_into_stock()
    {
        await StartNewGame();
        await Buy($"supply:{LavenderTopsId}", 50m);

        var runId = (await StartExtraction(50_000m)).Value.Value;

        (await Collect(runId)).Error.Code.ShouldBe("Production.NotReady");
        var running = (await GetProduction()).Value.ExtractionRuns.ShouldHaveSingleItem();
        running.MaterialName.ShouldBe("Lavender oil");
        running.OutputGrams.ShouldBe(500m);
        running.Timing.IsComplete.ShouldBeFalse();
        running.Timing.RemainingSeconds.ShouldBe(1200d);
        (await GetAtelier()).Value.Equipment.Single(e => e.Kind == "Still").Free.ShouldBe(0);

        _time.Advance(TimeSpan.FromMinutes(20));

        (await GetProduction()).Value.ExtractionRuns.Single().Timing.IsComplete.ShouldBeTrue();
        (await Collect(runId)).IsSuccess.ShouldBeTrue();
        (await GetProduction()).Value.ExtractionRuns.ShouldBeEmpty();
        (await GetAtelier()).Value.Equipment.Single(e => e.Kind == "Still").Free.ShouldBe(1);

        var oil = (await GetInventory(StockItemKind.Material)).Value.ShouldHaveSingleItem();
        oil.Name.ShouldBe("Lavender oil");
        oil.Quantity.ShouldBe(0.5m);
        oil.AverageUnitCost.ShouldBe(25m);
    }

    [Fact]
    public async Task Extraction_commands_return_their_errors()
    {
        await StartNewGame();

        (await Handle(new StartExtractionCommand("distil-moonbeams", 1000m))).Error.Code.ShouldBe("Extraction.RecipeNotFound");
        (await Handle(new StartExtractionCommand("Not A Slug", 1000m))).Error.ShouldBe(ExtractionErrors.RecipeIdMalformed);
        (await StartExtraction(-5m)).Error.Code.ShouldBe("Mass.Negative");
        (await StartExtraction(50_000m)).Error.ShouldBeOfType<InsufficientStockError>();
        (await Collect(Guid.NewGuid())).Error.Code.ShouldBe("Extraction.RunNotFound");

        await Buy($"supply:{LavenderTopsId}", 100m);
        (await StartExtraction(50_000m)).IsSuccess.ShouldBeTrue();
        (await StartExtraction(50_000m)).Error.Code.ShouldBe("Atelier.NoFreeEquipment");
    }

    [Fact]
    public async Task With_the_clock_set_back_no_job_gains_or_loses_progress()
    {
        await StartNewGame();
        await Buy($"supply:{LavenderTopsId}", 50m);
        var runId = (await StartExtraction(50_000m)).Value.Value;
        _time.Advance(TimeSpan.FromMinutes(5));
        await Buy($"supply:{SpiritId}", 1m);

        _clock.Override = Start.AddHours(-1);

        (await GetProduction()).Value.ExtractionRuns.Single().Timing.RemainingSeconds.ShouldBe(900d);
        (await Collect(runId)).Error.Code.ShouldBe("Production.NotReady");
        (await GetAtelier()).Value.Now.ShouldBe(Start.AddMinutes(5));

        // Jobs look frozen until real time catches up with the latest time seen.
        _clock.Override = Start.AddMinutes(4);
        (await GetProduction()).Value.ExtractionRuns.Single().Timing.RemainingSeconds.ShouldBe(900d);
        _clock.Override = Start.AddMinutes(20);
        (await Collect(runId)).IsSuccess.ShouldBeTrue();
    }

    // ---- compounding, product runs, bottling ----------------------------------------

    [Fact]
    public async Task A_formula_goes_from_stock_to_the_shelf()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();
        await Buy($"material:{BergamotId}", 0.15m);
        await Buy($"material:{RoseOttoId}", 0.015m);
        await Buy($"supply:{SpiritId}", 10m);
        await Buy($"supply:{FlaconId}", 49m);

        var batchId = (await Compound(formulaId, 300m)).Value.Value;

        var batch = (await GetProduction()).Value.ConcentrateBatches.ShouldHaveSingleItem();
        batch.FormulaName.ShouldBe("Rose water");
        batch.FormulaRevision.ShouldBe(3);
        batch.Status.ShouldBe("Maturing");
        (await StartRun(batchId, 300m)).Error.Code.ShouldBe("Production.NotReady");

        _time.Advance(TimeSpan.FromMinutes(15));
        (await GetProduction()).Value.ConcentrateBatches.Single().Status.ShouldBe("Ready");
        var runId = (await StartRun(batchId, 300m)).Value.Value;

        var run = (await GetProduction()).Value.ProductRuns.ShouldHaveSingleItem();
        run.ProductName.ShouldBe("Eau de Cologne");
        run.ConcentrationPercent.ShouldBe(3m);
        run.ProductGrams.ShouldBe(10_000m);
        (await GetProduction()).Value.ConcentrateBatches.Single().Status.ShouldBe("Exhausted");
        (await Bottle(runId)).Error.Code.ShouldBe("Production.NotReady");

        _time.Advance(TimeSpan.FromMinutes(30));
        (await Bottle(runId)).IsSuccess.ShouldBeTrue();

        var lot = (await GetShelf()).Value.ShouldHaveSingleItem();
        lot.Units.ShouldBe(49);
        lot.ProductRunId.ShouldBe(runId);
        lot.FormulaName.ShouldBe("Rose water");
        lot.BottledAt.ShouldBe(Start.AddMinutes(45));
        (await GetProduction()).Value.ProductRuns.ShouldBeEmpty();
        (await GetAtelier()).Value.Equipment.Single(e => e.Kind == "MacerationVessel").Free.ShouldBe(3);
    }

    [Fact]
    public async Task Production_commands_return_their_errors()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();
        var empty = Formula.Create("Empty").Value;
        _session.Add(empty);

        (await Compound(Guid.NewGuid(), 300m)).Error.Code.ShouldBe("Formula.NotFound");
        (await Compound(empty.Id.Value, 300m)).Error.ShouldBe(FormulaErrors.EmptyFormula);
        (await Compound(formulaId, 0m)).Error.Code.ShouldBe("Mass.Zero");
        (await Compound(formulaId, 300m)).Error.ShouldBeOfType<InsufficientStockError>().Shortages.Count.ShouldBe(3);
        (await StartRun(Guid.NewGuid(), 300m)).Error.Code.ShouldBe("Production.BatchNotFound");
        (await Bottle(Guid.NewGuid())).Error.Code.ShouldBe("Production.RunNotFound");

        await Buy($"material:{BergamotId}", 0.15m);
        await Buy($"material:{RoseOttoId}", 0.015m);
        await Buy($"supply:{SpiritId}", 10m);
        var batchId = (await Compound(formulaId, 300m)).Value.Value;
        _time.Advance(TimeSpan.FromMinutes(15));

        (await Handle(new StartProductRunCommand(batchId, "eau-de-moonbeam", 300m))).Error.Code.ShouldBe("Production.ProductTypeNotFound");
        (await Handle(new StartProductRunCommand(batchId, FakeProductTypeCatalog.CologneId, 300m, 12m)))
            .Error.Code.ShouldBe("Production.ConcentrationOutOfRange");
        (await Handle(new StartProductRunCommand(batchId, FakeProductTypeCatalog.CologneId, 300m, 0m)))
            .Error.ShouldBe(ProductionErrors.ConcentrationInvalid);
    }

    [Fact]
    public async Task A_failed_command_leaves_every_aggregate_unchanged_and_writes_no_save()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();
        await Buy($"material:{BergamotId}", 0.15m);
        await Buy($"material:{RoseOttoId}", 0.015m);
        await Buy($"supply:{SpiritId}", 1m);
        var batchId = (await Compound(formulaId, 300m)).Value.Value;
        _time.Advance(TimeSpan.FromMinutes(15));
        _session.Atelier!.Observe(_time.GetUtcNow()); // Seeing the time pass is not a change the command makes.
        var before = Json(_session.ToSnapshot());

        // Enough concentrate and a free vessel, but only a little of the 9.7 kg of spirit needed.
        var handler = new SaveGameDecorator<StartProductRunCommand, Result<ProductRunId>>(
            new StartProductRunCommandHandler(_session, _clock, _productTypes, _items, _settings), _session, _saver, _clock);
        var result = await handler.Handle(new StartProductRunCommand(batchId, FakeProductTypeCatalog.CologneId, 300m), Ct);

        result.Error.ShouldBeOfType<InsufficientStockError>();
        Json(_session.ToSnapshot()).ShouldBe(before);
        _saver.Saves.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_save_decorator_saves_after_success_only_and_only_once_a_game_is_running()
    {
        var purchase = new SaveGameDecorator<PurchaseSuppliesCommand, Result>(
            new PurchaseSuppliesCommandHandler(_session, _clock, _items), _session, _saver, _clock);

        await purchase.Handle(new PurchaseSuppliesCommand($"supply:{SpiritId}", 1m), Ct);
        _saver.Saves.ShouldBeEmpty();

        await StartNewGame();
        await purchase.Handle(new PurchaseSuppliesCommand($"supply:{SpiritId}", 1m), Ct);
        await purchase.Handle(new PurchaseSuppliesCommand("supply:moon-dust", 1m), Ct);

        _saver.Saves.ShouldHaveSingleItem().Atelier.Cash.ShouldBe(4997m);
        _session.PruneCount.ShouldBe(1);
    }

    // ---- quote ----------------------------------------------------------------------

    [Fact]
    public async Task A_quote_lists_every_shortage_and_what_it_would_cost()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();
        await Buy($"material:{BergamotId}", 1m);

        var quote = (await Quote(formulaId, 300m, FakeProductTypeCatalog.CologneId)).Value;

        quote.CanProduce.ShouldBeFalse();
        quote.ConcentrateGrams.ShouldBe(300m);
        quote.MaturationSeconds.ShouldBe(900d);
        quote.Requirements.Select(line => (line.Item, line.Required, line.Missing)).ShouldBe(
        [
            ($"material:{BergamotId}", 0.15m, 0m),
            ($"material:{RoseOttoId}", 0.015m, 0.015m),
            ($"supply:{FlaconId}", 49m, 49m),
            // 0.135 kg of carrier plus 9.7 kg for dilution.
            ($"supply:{SpiritId}", 9.835m, 9.835m),
        ]);
        // 0.015 × 2400 + 49 × 0.40 + 9.835 × 3, each rounded to the centime.
        quote.CostToBuyMissing.ShouldBe(36m + 19.6m + 29.51m);
        quote.Product!.Units.ShouldBe(49);
        quote.Product.ProductGrams.ShouldBe(10_000m);
        quote.Product.LossGrams.ShouldBe(200m);
        quote.Product.MacerationSeconds.ShouldBe(1800d);
        (await GetInventory()).Value.ShouldHaveSingleItem().Quantity.ShouldBe(1m);
    }

    [Fact]
    public async Task A_quote_without_a_product_covers_compounding_only()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();

        var quote = (await Quote(formulaId, 300m)).Value;

        quote.Product.ShouldBeNull();
        quote.Requirements.Count.ShouldBe(3);
        // 0.15 × 40 + 0.015 × 2400 + 0.135 × 3 at catalog prices.
        quote.ConcentrateCost.ShouldBe(42.41m);
    }

    [Fact]
    public async Task A_quote_rejects_what_the_real_commands_would_reject()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();

        (await Quote(Guid.NewGuid(), 300m)).Error.Code.ShouldBe("Formula.NotFound");
        (await Quote(formulaId, 0m)).Error.Code.ShouldBe("Mass.Zero");
        (await Quote(formulaId, 300m, "eau-de-moonbeam")).Error.Code.ShouldBe("Production.ProductTypeNotFound");
        (await Quote(formulaId, 300m, FakeProductTypeCatalog.CologneId, 9m)).Error.Code.ShouldBe("Production.ConcentrationOutOfRange");
        (await Quote(formulaId, 900m, FakeProductTypeCatalog.CologneId)).Error.Code.ShouldBe("Production.OverCapacity");
    }

    [Fact]
    public async Task A_quote_agrees_with_what_the_real_commands_then_consume_and_cost()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();
        await Buy($"supply:{LavenderTopsId}", 10m);

        var shopping = (await Quote(formulaId, 300m, FakeProductTypeCatalog.CologneId, 4m)).Value;
        foreach (var line in shopping.Requirements)
        {
            (await Buy(line.Item, line.Missing)).IsSuccess.ShouldBeTrue();
        }

        var quote = (await Quote(formulaId, 300m, FakeProductTypeCatalog.CologneId, 4m)).Value;
        quote.CanProduce.ShouldBeTrue();
        quote.CostToBuyMissing.ShouldBe(0m);

        var batchId = (await Compound(formulaId, 300m)).Value.Value;
        (await GetProduction()).Value.ConcentrateBatches.Single().TotalCost.ShouldBe(quote.ConcentrateCost);
        _time.Advance(TimeSpan.FromMinutes(15));
        var runId = (await Handle(new StartProductRunCommand(batchId, FakeProductTypeCatalog.CologneId, 300m, 4m))).Value.Value;
        _time.Advance(TimeSpan.FromMinutes(30));
        (await Bottle(runId)).IsSuccess.ShouldBeTrue();

        var lot = (await GetShelf()).Value.Single();
        lot.Units.ShouldBe(quote.Product!.Units);
        lot.UnitCost.ShouldBe(quote.Product.UnitCost);
        // Everything quoted was consumed, and nothing else was touched.
        (await GetInventory()).Value.ShouldHaveSingleItem().Item.ShouldBe($"supply:{LavenderTopsId}");
    }

    // ---- queries --------------------------------------------------------------------

    [Fact]
    public async Task GetAtelier_reports_cash_equipment_and_the_latest_ledger_entries_newest_first()
    {
        await StartNewGame();
        await Buy($"supply:{SpiritId}", 1m);
        _time.Advance(TimeSpan.FromMinutes(1));
        await Buy($"supply:{FlaconId}", 10m);

        var atelier = (await new GetAtelierQueryHandler(_session, _clock, _settings).Handle(new GetAtelierQuery(2), Ct)).Value;

        atelier.Cash.ShouldBe(4993m);
        atelier.Ledger.Select(entry => entry.Amount).ShouldBe([-4m, -3m]);
        atelier.Ledger[0].Category.ShouldBe("SuppliesPurchased");
        atelier.Equipment.Select(e => (e.Kind, e.Count, e.InUse, e.CapacityKg))
            .ShouldBe([("Still", 1, 0, 100m), ("TinctureJar", 2, 0, 10m), ("MacerationVessel", 3, 0, 20m)]);
    }

    [Fact]
    public async Task GetInventory_filters_by_kind()
    {
        await StartNewGame();
        await Buy($"supply:{SpiritId}", 2m);
        await Buy($"material:{BergamotId}", 0.5m);

        (await GetInventory()).Value.Count.ShouldBe(2);
        var supplies = (await GetInventory(StockItemKind.Supply)).Value.ShouldHaveSingleItem();
        supplies.ShouldBe(new StockLineResponse($"supply:{SpiritId}", "Spirit of wine", "Kilogram", 2m, 3m, 6m));
    }

    // ---- helpers --------------------------------------------------------------------

    private Guid SeedRoseAndBergamot()
    {
        var formula = Formula.Create("Rose water").Value;
        formula.AddLine(_materials.Find(MaterialId.Create(BergamotId).Value)!, Parts.Create(10m).Value);
        formula.AddLine(_materials.Find(MaterialId.Create(RoseOttoId).Value)!, Parts.Create(10m).Value, Dilution.FromPercent(10m).Value);
        _session.Add(formula);
        return formula.Id.Value;
    }

    /// <summary>Leaves a running extraction, a part-used batch and a macerating run.</summary>
    private async Task<Guid> PlayToMaceratingRun()
    {
        await StartNewGame();
        var formulaId = SeedRoseAndBergamot();
        await Buy($"supply:{LavenderTopsId}", 50m);
        await Buy($"material:{BergamotId}", 0.15m);
        await Buy($"material:{RoseOttoId}", 0.015m);
        await Buy($"supply:{SpiritId}", 10m);
        await StartExtraction(50_000m);
        var batchId = (await Compound(formulaId, 300m)).Value.Value;
        _time.Advance(TimeSpan.FromMinutes(15));
        (await StartRun(batchId, 100m)).IsSuccess.ShouldBeTrue();
        return formulaId;
    }

    private GameRestorer Restorer() => new(_materials, _items, _recipes, _productTypes);

    private static string Json(GameSnapshot snapshot) => System.Text.Json.JsonSerializer.Serialize(snapshot);

    private Task<Result> StartNewGame() =>
        new StartNewGameCommandHandler(_session, _saver, _clock, _settings).Handle(new StartNewGameCommand(), Ct);

    private Task<Result<LoadGameOutcome>> Load() =>
        new LoadGameCommandHandler(_session, _saver, Restorer()).Handle(new LoadGameCommand(), Ct);

    private Task<Result> Buy(string item, decimal quantity) =>
        new PurchaseSuppliesCommandHandler(_session, _clock, _items).Handle(new PurchaseSuppliesCommand(item, quantity), Ct);

    private Task<Result<ExtractionRunId>> StartExtraction(decimal rawGrams) =>
        Handle(new StartExtractionCommand(FakeExtractionRecipeCatalog.DistilLavenderId, rawGrams));

    private Task<Result<ExtractionRunId>> Handle(StartExtractionCommand command) =>
        new StartExtractionCommandHandler(_session, _clock, _recipes, _items, _settings).Handle(command, Ct);

    private Task<Result> Collect(Guid runId) =>
        new CollectExtractionCommandHandler(_session, _clock).Handle(new CollectExtractionCommand(runId), Ct);

    private Task<Result<ConcentrateBatchId>> Compound(Guid formulaId, decimal grams) =>
        new CompoundConcentrateCommandHandler(_session, _clock, _items, _settings)
            .Handle(new CompoundConcentrateCommand(formulaId, grams), Ct);

    private Task<Result<ProductRunId>> StartRun(Guid batchId, decimal grams) =>
        Handle(new StartProductRunCommand(batchId, FakeProductTypeCatalog.CologneId, grams));

    private Task<Result<ProductRunId>> Handle(StartProductRunCommand command) =>
        new StartProductRunCommandHandler(_session, _clock, _productTypes, _items, _settings).Handle(command, Ct);

    private Task<Result> Bottle(Guid runId) =>
        new BottleProductRunCommandHandler(_session, _clock, _productTypes, _settings)
            .Handle(new BottleProductRunCommand(runId), Ct);

    private Task<Result<AtelierResponse>> GetAtelier() =>
        new GetAtelierQueryHandler(_session, _clock, _settings).Handle(new GetAtelierQuery(), Ct);

    private Task<Result<IReadOnlyList<StockLineResponse>>> GetInventory(StockItemKind? kind = null) =>
        new GetInventoryQueryHandler(_session, _items).Handle(new GetInventoryQuery(kind), Ct);

    private Task<Result<ProductionResponse>> GetProduction() =>
        new GetProductionQueryHandler(_session, _clock, _materials, _productTypes).Handle(new GetProductionQuery(), Ct);

    private Task<Result<IReadOnlyList<ShelfLotResponse>>> GetShelf() =>
        new GetShelfQueryHandler(_session, _productTypes).Handle(new GetShelfQuery(), Ct);

    private Task<Result<ProductionQuoteResponse>> Quote(
        Guid formulaId,
        decimal grams,
        string? productTypeId = null,
        decimal? concentrationPercent = null) =>
        new QuoteProductionQueryHandler(_session, _productTypes, _items, _settings)
            .Handle(new QuoteProductionQuery(formulaId, grams, productTypeId, concentrationPercent), Ct);
}
