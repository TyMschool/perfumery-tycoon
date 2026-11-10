using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using static Perfumery.Domain.UnitTests.TestProduction;

namespace Perfumery.Domain.UnitTests.Production;

public class ProductionTests
{
    private static readonly StockItemId Bergamot = StockItemId.For(TestMaterials.Bergamot.Id);
    private static readonly StockItemId RoseOtto = StockItemId.For(TestMaterials.RoseOtto.Id);

    // ---- value objects and reference data -------------------------------------------

    [Fact]
    public void Production_value_objects_reject_out_of_range_input()
    {
        Concentration.FromPercent(0m).Error.ShouldBe(ProductionErrors.ConcentrationInvalid);
        Concentration.FromPercent(100m).Error.ShouldBe(ProductionErrors.ConcentrationInvalid);
        Concentration.FromPercent(3m).Value.Fraction.ShouldBe(0.03m);
        Yield.Create(0m).Error.ShouldBe(ExtractionErrors.YieldOutOfRange);
        JobTiming.Create(Start, Start.AddSeconds(-1)).Error.ShouldBe(ProductionErrors.TimingInvalid);
        FormulaRevisionRef.Create(FormulaId.New(), 0).Error.ShouldBe(FormulaErrors.InvalidRevision);
        ExtractionRecipeId.Create("Distil Lavender").Error.ShouldBe(ExtractionErrors.RecipeIdMalformed);
        ProductTypeId.Create("").Error.ShouldBe(ProductionErrors.ProductTypeIdMalformed);
    }

    [Fact]
    public void A_job_is_incomplete_one_tick_before_its_completion_time_and_complete_at_it()
    {
        var timing = JobTiming.Starting(Start, TimeSpan.FromMinutes(20));
        var due = Start.AddMinutes(20);

        timing.IsComplete(due.AddTicks(-1)).ShouldBeFalse();
        timing.Remaining(due.AddTicks(-1)).ShouldBe(TimeSpan.FromTicks(1));
        timing.IsComplete(due).ShouldBeTrue();
        timing.Remaining(due.AddDays(3)).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_product_type_must_have_a_sane_concentration_range()
    {
        ProductType.Create(
                Cologne.Id, "Cologne", Percent(5m), Percent(2m), Percent(3m), TimeSpan.Zero, FlaconId, Grams(200m))
            .Error.ShouldBe(ProductionErrors.ProductTypeInvalid);
        ProductType.Create(
                Cologne.Id, "Cologne", Percent(2m), Percent(5m), Percent(7m), TimeSpan.Zero, FlaconId, Grams(200m))
            .Error.ShouldBe(ProductionErrors.ProductTypeInvalid);
    }

    // ---- purchasing -----------------------------------------------------------------

    [Fact]
    public void Purchasing_spends_cash_and_stocks_at_the_price_paid()
    {
        var atelier = Atelier.Open(Francs(100m), Start);
        var inventory = Inventory.Create();

        PurchasingService.Purchase(atelier, inventory, Spirit, Qty(10m), Francs(3m), Start).IsSuccess.ShouldBeTrue();

        atelier.Cash.Amount.ShouldBe(70m);
        inventory.QuantityOf(Spirit).Value.ShouldBe(10m);
        inventory.AverageUnitCost(Spirit).Amount.ShouldBe(3m);
        atelier.DomainEvents.OfType<SuppliesPurchasedDomainEvent>().ShouldHaveSingleItem().Amount.ShouldBe(30m);
    }

    [Fact]
    public void A_purchase_the_atelier_cannot_afford_changes_nothing()
    {
        var atelier = Atelier.Open(Francs(10m), Start);
        var inventory = Inventory.Create();

        var result = PurchasingService.Purchase(atelier, inventory, Spirit, Qty(10m), Francs(3m), Start);

        result.Error.Code.ShouldBe("Atelier.InsufficientFunds");
        atelier.Cash.Amount.ShouldBe(10m);
        inventory.Lines.ShouldBeEmpty();
    }

    // ---- extraction -----------------------------------------------------------------

    [Fact]
    public void Distilling_consumes_the_raw_botanical_and_yields_mass_times_yield()
    {
        var inventory = Stocked((LavenderTops, 60m, 0.25m));

        var run = ExtractionService.Start(inventory, DistilLavender, Kilograms(50m), Settings, 0, Spirit, Start).Value;

        inventory.QuantityOf(LavenderTops).Value.ShouldBe(10m);
        run.OutputMass.ShouldBe(Grams(500m));
        run.InputCost.Amount.ShouldBe(12.5m);
        run.Timing.CompletesAt.ShouldBe(Start.AddMinutes(20));
        run.OccupiesEquipment.ShouldBeTrue();
        run.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ExtractionStartedDomainEvent>();
    }

    [Fact]
    public void Collecting_early_fails_and_collecting_on_time_stocks_the_material_at_input_cost()
    {
        var inventory = Stocked((LavenderTops, 50m, 0.25m));
        var run = ExtractionService.Start(inventory, DistilLavender, Kilograms(50m), Settings, 0, Spirit, Start).Value;

        ExtractionService.Collect(run, inventory, Start.AddMinutes(19)).Error.Code.ShouldBe("Production.NotReady");
        inventory.QuantityOf(StockItemId.For(LavenderOil)).IsZero.ShouldBeTrue();

        var output = ExtractionService.Collect(run, inventory, Start.AddMinutes(20)).Value;

        output.Mass.ShouldBe(Grams(500m));
        inventory.QuantityOf(StockItemId.For(LavenderOil)).Value.ShouldBe(0.5m);
        // 12.50 francs of tops ÷ 0.5 kg of oil.
        inventory.AverageUnitCost(StockItemId.For(LavenderOil)).Amount.ShouldBe(25m);
        run.OccupiesEquipment.ShouldBeFalse();
        ExtractionService.Collect(run, inventory, Start.AddHours(1)).Error.ShouldBe(ExtractionErrors.AlreadyCollected);
    }

    [Fact]
    public void A_tincture_takes_spirit_too_and_its_cost_includes_it()
    {
        var inventory = Stocked((BenzoinResin, 1m, 30m), (Spirit, 5m, 3m));

        var run = ExtractionService.Start(inventory, TinctureBenzoin, Kilograms(1m), Settings, 0, Spirit, Start).Value;

        inventory.Lines.ShouldBeEmpty();
        run.OutputMass.ShouldBe(Kilograms(5m));
        run.InputCost.Amount.ShouldBe(45m);
        run.Timing.CompletesAt.ShouldBe(Start.AddHours(3));
    }

    [Fact]
    public void A_tincture_charge_is_checked_as_raw_plus_spirit_against_the_jar()
    {
        // The jar holds 10 kg. 1.5 kg of resin is fine alone, but with 7.5 kg of spirit it is 9 kg;
        // 2 kg of resin needs 10 kg of spirit and makes a 12 kg charge.
        var inventory = Stocked((BenzoinResin, 5m, 30m), (Spirit, 50m, 3m));

        ExtractionService.Start(inventory, TinctureBenzoin, Kilograms(1.5m), Settings, 0, Spirit, Start).IsSuccess.ShouldBeTrue();

        var over = ExtractionService.Start(inventory, TinctureBenzoin, Kilograms(2m), Settings, 1, Spirit, Start);

        over.Error.Code.ShouldBe("Production.OverCapacity");
        inventory.QuantityOf(BenzoinResin).Value.ShouldBe(3.5m);
        inventory.QuantityOf(Spirit).Value.ShouldBe(42.5m);
    }

    [Fact]
    public void Extraction_fails_without_changing_stock_when_anything_is_wrong()
    {
        var inventory = Stocked((LavenderTops, 150m, 0.25m));

        ExtractionService.Start(inventory, DistilLavender, Kilograms(101m), Settings, 0, Spirit, Start)
            .Error.Code.ShouldBe("Production.OverCapacity");
        ExtractionService.Start(inventory, DistilLavender, Kilograms(50m), Settings, 1, Spirit, Start)
            .Error.Code.ShouldBe("Atelier.NoFreeEquipment");
        ExtractionService.Start(inventory, DistilLavender, Mass.Zero, Settings, 0, Spirit, Start)
            .Error.ShouldBe(MassErrors.ZeroNotAllowed);
        ExtractionService.Start(inventory, DistilLavender, Grams(0.4m), Settings, 0, Spirit, Start)
            .Error.ShouldBe(ExtractionErrors.ChargeTooSmall);
        ExtractionService.Start(Inventory.Create(), DistilLavender, Kilograms(50m), Settings, 0, Spirit, Start)
            .Error.ShouldBeOfType<InsufficientStockError>();

        inventory.QuantityOf(LavenderTops).Value.ShouldBe(150m);
    }

    [Fact]
    public void The_time_scale_shortens_new_jobs_and_leaves_running_ones_alone()
    {
        var inventory = Stocked((BenzoinResin, 2m, 30m), (Spirit, 10m, 3m));
        var normal = ExtractionService.Start(inventory, TinctureBenzoin, Kilograms(1m), Settings, 0, Spirit, Start).Value;

        var fast = ExtractionService.Start(
            inventory, TinctureBenzoin, Kilograms(1m), SettingsWith(timeScale: 60m), 1, Spirit, Start).Value;

        fast.Timing.CompletesAt.ShouldBe(Start.AddMinutes(3));
        normal.Timing.CompletesAt.ShouldBe(Start.AddHours(3));
    }

    // ---- compounding ----------------------------------------------------------------

    [Fact]
    public void Compounding_consumes_each_material_and_the_carrier_spirit()
    {
        // 10 parts bergamot neat + 10 parts rose otto at 10%: neat fractions 0.5 and 0.05, carrier 0.45.
        var inventory = Stocked((Bergamot, 1m, 40m), (RoseOtto, 0.1m, 2400m), (Spirit, 1m, 3m));
        var (formulaRef, composition) = RoseAndBergamot();

        var batch = CompoundingService.Compound(inventory, formulaRef, composition, Grams(200m), Settings, Spirit, Start).Value;

        inventory.QuantityOf(Bergamot).Value.ShouldBe(0.9m);
        inventory.QuantityOf(RoseOtto).Value.ShouldBe(0.09m);
        inventory.QuantityOf(Spirit).Value.ShouldBe(0.91m);
        batch.MassMade.ShouldBe(Grams(200m));
        // 0.1 kg × 40 + 0.01 kg × 2400 + 0.09 kg × 3.
        batch.TotalCost.Amount.ShouldBe(4m + 24m + 0.27m);
        batch.Timing.CompletesAt.ShouldBe(Start.AddMinutes(15));
        batch.Formula.ShouldBe(formulaRef);
    }

    [Fact]
    public void Compounding_with_a_shortage_consumes_nothing_and_lists_every_shortage()
    {
        var inventory = Stocked((Bergamot, 1m, 40m));
        var (formulaRef, composition) = RoseAndBergamot();

        var result = CompoundingService.Compound(inventory, formulaRef, composition, Grams(200m), Settings, Spirit, Start);

        result.Error.ShouldBeOfType<InsufficientStockError>()
            .Shortages.Select(shortage => shortage.Item).ShouldBe([RoseOtto, Spirit], ignoreOrder: true);
        inventory.QuantityOf(Bergamot).Value.ShouldBe(1m);
    }

    [Fact]
    public void A_batch_too_small_to_weigh_every_material_is_rejected()
    {
        var (_, composition) = RoseAndBergamot();

        CompoundingService.Plan(composition, Grams(0.05m), Spirit).Error.ShouldBe(ProductionErrors.BatchTooSmall);
        CompoundingService.Plan(composition, Mass.Zero, Spirit).Error.ShouldBe(MassErrors.ZeroNotAllowed);
    }

    [Fact]
    public void A_batch_keeps_its_composition_when_the_formula_is_revised_afterwards()
    {
        var formula = Formula.Create("Rose and bergamot").Value;
        formula.AddLine(TestMaterials.Bergamot, TestMaterials.Parts(10m));
        formula.AddLine(TestMaterials.RoseOtto, TestMaterials.Parts(10m));
        var inventory = Stocked((Bergamot, 1m, 40m), (RoseOtto, 0.1m, 2400m), (Spirit, 1m, 3m));
        var batch = CompoundingService.Compound(
            inventory, formula.CurrentRevision, formula.Compose().Value, Grams(200m), Settings, Spirit, Start).Value;

        formula.AdjustParts(TestMaterials.Bergamot.Id, TestMaterials.Parts(500m));
        formula.RemoveLine(TestMaterials.RoseOtto.Id);

        batch.Formula.Revision.ShouldBe(3);
        formula.Revision.ShouldBe(5);
        batch.Composition.Lines.ShouldBe(
        [
            new CompositionLine(TestMaterials.Bergamot.Id, 0.5m),
            new CompositionLine(TestMaterials.RoseOtto.Id, 0.05m),
        ]);
    }

    [Fact]
    public void Drawing_before_maturation_or_beyond_what_remains_fails()
    {
        var batch = ReadyBatch(Grams(300m), Francs(30m), out _);

        batch.Draw(Grams(100m), Start.AddMinutes(14)).Error.Code.ShouldBe("Production.NotReady");
        batch.Draw(Grams(301m), Start.AddMinutes(15)).Error.Code.ShouldBe("Production.NotEnoughConcentrate");
        batch.Draw(Mass.Zero, Start.AddMinutes(15)).Error.ShouldBe(MassErrors.ZeroNotAllowed);
        batch.MassRemaining.ShouldBe(Grams(300m));
        batch.StatusAt(Start).ShouldBe(ConcentrateStatus.Maturing);
        batch.StatusAt(Start.AddMinutes(15)).ShouldBe(ConcentrateStatus.Ready);
    }

    [Fact]
    public void Drawing_takes_a_proportional_share_of_the_cost_and_the_last_draw_takes_the_rest()
    {
        var batch = ReadyBatch(Grams(300m), Francs(10m), out _);
        var ready = Start.AddMinutes(15);

        var first = batch.Draw(Grams(100m), ready).Value;
        var second = batch.Draw(Grams(100m), ready).Value;
        var last = batch.Draw(Grams(100m), ready).Value;

        first.Amount.ShouldBe(10m / 3m, 0.0000001m);
        (first.Amount + second.Amount + last.Amount).ShouldBe(10m);
        batch.CostRemaining.ShouldBe(Money.Zero);
        batch.StatusAt(ready).ShouldBe(ConcentrateStatus.Exhausted);
    }

    // ---- product runs ---------------------------------------------------------------

    [Fact]
    public void The_worked_example_300_g_at_3_percent_gives_10_kg_and_49_flacons()
    {
        var batch = ReadyBatch(Grams(300m), Francs(30m), out var inventory);
        inventory.Receive(Spirit, Qty(9.7m), Francs(29.1m));
        inventory.Receive(Flacons, Qty(60m), Francs(24m));
        var ready = Start.AddMinutes(15);

        var run = BottlingService.StartRun(
            inventory, batch, Cologne, Percent(3m), Grams(300m), Settings, 0, Spirit, ready).Value;

        run.ProductMass.ShouldBe(Kilograms(10m));
        inventory.QuantityOf(Spirit).IsZero.ShouldBeTrue();
        run.Cost.Amount.ShouldBe(30m + 29.1m);
        run.Timing.CompletesAt.ShouldBe(ready.AddMinutes(30));
        run.OccupiesVessel.ShouldBeTrue();

        var lot = BottlingService.Bottle(run, inventory, Cologne, Settings, ready.AddMinutes(30)).Value;

        // 10 kg less 2% is 9.8 kg: 49 flacons of 200 g. The other 200 g is loss.
        lot.Units.ShouldBe(49);
        run.Loss.ShouldBe(Grams(200m));
        inventory.QuantityOf(Flacons).Value.ShouldBe(11m);
        // (30 + 29.10 + 49 × 0.40) ÷ 49.
        lot.UnitCost.Amount.ShouldBe((30m + 29.1m + 19.6m) / 49m);
        inventory.Shelf.ShouldHaveSingleItem().ShouldBe(lot);
        inventory.ShelfValue.Amount.ShouldBe(78.7m, 0.0000001m);
        run.OccupiesVessel.ShouldBeFalse();
        lot.Formula.ShouldBe(batch.Formula);
        lot.Composition.ShouldBe(batch.Composition);
    }

    [Fact]
    public void One_batch_can_feed_several_runs()
    {
        var batch = ReadyBatch(Grams(300m), Francs(30m), out var inventory);
        inventory.Receive(Spirit, Qty(20m), Francs(60m));
        var ready = Start.AddMinutes(15);

        var first = BottlingService.StartRun(inventory, batch, Cologne, Percent(3m), Grams(150m), Settings, 0, Spirit, ready);
        var second = BottlingService.StartRun(inventory, batch, Cologne, Percent(5m), Grams(150m), Settings, 1, Spirit, ready);

        first.Value.Cost.Amount.ShouldBe(15m + (4.85m * 3m));
        second.Value.ProductMass.ShouldBe(Kilograms(3m));
        batch.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void A_product_run_fails_without_changing_anything_when_anything_is_wrong()
    {
        var batch = ReadyBatch(Grams(900m), Francs(90m), out var inventory);
        inventory.Receive(Spirit, Qty(5m), Francs(15m));
        var ready = Start.AddMinutes(15);

        Perfumery.SharedKernel.Result<ProductRun> Try(decimal percent, decimal grams, int vesselsInUse, DateTimeOffset now) =>
            BottlingService.StartRun(inventory, batch, Cologne, Percent(percent), Grams(grams), Settings, vesselsInUse, Spirit, now);

        Try(6m, 300m, 0, ready).Error.Code.ShouldBe("Production.ConcentrationOutOfRange");
        Try(1m, 300m, 0, ready).Error.Code.ShouldBe("Production.ConcentrationOutOfRange");
        Try(3m, 900m, 0, ready).Error.Code.ShouldBe("Production.OverCapacity");
        Try(3m, 300m, 3, ready).Error.Code.ShouldBe("Atelier.NoFreeEquipment");
        Try(3m, 300m, 0, Start).Error.Code.ShouldBe("Production.NotReady");
        Try(5m, 950m, 0, ready).Error.Code.ShouldBe("Production.NotEnoughConcentrate");
        Try(3m, 300m, 0, ready).Error.ShouldBeOfType<InsufficientStockError>();

        batch.MassRemaining.ShouldBe(Grams(900m));
        batch.CostRemaining.Amount.ShouldBe(90m);
        inventory.QuantityOf(Spirit).Value.ShouldBe(5m);
    }

    [Fact]
    public void Bottling_fails_before_maceration_ends_or_without_enough_flacons()
    {
        var batch = ReadyBatch(Grams(300m), Francs(30m), out var inventory);
        inventory.Receive(Spirit, Qty(9.7m), Francs(29.1m));
        inventory.Receive(Flacons, Qty(48m), Francs(19.2m));
        var ready = Start.AddMinutes(15);
        var run = BottlingService.StartRun(inventory, batch, Cologne, Percent(3m), Grams(300m), Settings, 0, Spirit, ready).Value;

        BottlingService.Bottle(run, inventory, Cologne, Settings, ready.AddMinutes(29)).Error.Code.ShouldBe("Production.NotReady");
        BottlingService.Bottle(run, inventory, Cologne, Settings, ready.AddMinutes(30))
            .Error.ShouldBeOfType<InsufficientStockError>().Shortages.ShouldHaveSingleItem().Missing.ShouldBe(1m);

        run.Status.ShouldBe(ProductRunStatus.Macerating);
        inventory.QuantityOf(Flacons).Value.ShouldBe(48m);
        inventory.Shelf.ShouldBeEmpty();

        inventory.Receive(Flacons, Qty(1m), Francs(0.4m));
        BottlingService.Bottle(run, inventory, Cologne, Settings, ready.AddMinutes(30)).IsSuccess.ShouldBeTrue();
        BottlingService.Bottle(run, inventory, Cologne, Settings, ready.AddHours(1)).Error.ShouldBe(ProductionErrors.AlreadyBottled);
    }

    [Fact]
    public void A_run_too_small_to_fill_one_flacon_cannot_be_bottled()
    {
        var batch = ReadyBatch(Grams(300m), Francs(30m), out var inventory);
        inventory.Receive(Spirit, Qty(1m), Francs(3m));
        inventory.Receive(Flacons, Qty(5m), Francs(2m));
        var ready = Start.AddMinutes(15);
        var run = BottlingService.StartRun(inventory, batch, Cologne, Percent(5m), Grams(5m), Settings, 0, Spirit, ready).Value;

        BottlingService.Bottle(run, inventory, Cologne, Settings, ready.AddHours(1)).Error.ShouldBe(ProductionErrors.NothingToBottle);
    }

    // ---- conservation ---------------------------------------------------------------

    [Fact]
    public void Mass_and_cost_are_conserved_from_supplies_to_the_shelf()
    {
        var random = new Random(1851);
        for (var round = 0; round < 150; round++)
        {
            var bergamotPrice = random.Next(1000, 9000) / 100m;
            var rosePrice = random.Next(100_000, 400_000) / 100m;
            var spiritPrice = random.Next(100, 900) / 100m;
            var flaconPrice = random.Next(10, 90) / 100m;
            var inventory = Stocked(
                (Bergamot, 5m, bergamotPrice), (RoseOtto, 1m, rosePrice), (Spirit, 60m, spiritPrice), (Flacons, 200m, flaconPrice));
            var paid = inventory.StockValue.Amount;
            var stockBefore = MassInStock(inventory);

            var formula = Formula.Create("Random").Value;
            formula.AddLine(TestMaterials.Bergamot, TestMaterials.Parts(random.Next(1, 100_000) / 100m));
            formula.AddLine(
                TestMaterials.RoseOtto,
                TestMaterials.Parts(random.Next(1, 100_000) / 100m),
                TestMaterials.Percent(random.Next(1, 101)));
            var requested = Grams(random.Next(2000, 60_000) / 100m);

            var compounded = CompoundingService.Compound(
                inventory, formula.CurrentRevision, formula.Compose().Value, requested, Settings, Spirit, Start);
            if (compounded.IsFailure)
            {
                // A batch too small to weigh a trace material: nothing may have moved.
                compounded.Error.ShouldBe(ProductionErrors.BatchTooSmall);
                inventory.StockValue.Amount.ShouldBe(paid);
                continue;
            }

            var batch = compounded.Value;
            (stockBefore - MassInStock(inventory)).ShouldBe(batch.MassMade.Kilograms);
            (batch.TotalCost.Amount + inventory.StockValue.Amount).ShouldBe(paid, 0.000000001m);

            var ready = Start.AddMinutes(15);
            var drawn = batch.MassMade.Times(random.Next(20, 101) / 100m);
            var concentration = Percent(random.Next(200, 501) / 100m);
            var stockBeforeRun = MassInStock(inventory);
            var run = BottlingService.StartRun(inventory, batch, Cologne, concentration, drawn, Settings, 0, Spirit, ready).Value;

            (drawn.Kilograms + (stockBeforeRun - MassInStock(inventory))).ShouldBe(run.ProductMass.Kilograms);
            (batch.MassRemaining + drawn).ShouldBe(batch.MassMade);

            var bottled = BottlingService.Bottle(run, inventory, Cologne, Settings, ready.AddMinutes(30));
            if (bottled.IsFailure)
            {
                bottled.Error.ShouldBe(ProductionErrors.NothingToBottle);
                continue;
            }

            (Cologne.FlaconFill.Times(bottled.Value.Units) + run.Loss).ShouldBe(run.ProductMass);
            run.Loss.Grams.ShouldBeGreaterThanOrEqualTo(run.ProductMass.Grams * Settings.FilterLoss - 0.01m);

            // Everything paid for is now in bottles, in what is left of the batch, or still in stock.
            (inventory.ShelfValue.Amount + batch.CostRemaining.Amount + inventory.StockValue.Amount)
                .ShouldBe(paid, 0.000000001m);
        }
    }

    // ---- snapshots ------------------------------------------------------------------

    [Fact]
    public void Jobs_survive_a_snapshot_round_trip_at_every_stage()
    {
        var inventory = Stocked((LavenderTops, 50m, 0.25m));
        var extraction = ExtractionService.Start(inventory, DistilLavender, Kilograms(50m), Settings, 0, Spirit, Start).Value;
        var batch = ReadyBatch(Grams(300m), Francs(30m), out var stock);
        stock.Receive(Spirit, Qty(20m), Francs(60m));
        stock.Receive(Flacons, Qty(60m), Francs(24m));
        var ready = Start.AddMinutes(15);
        var macerating = BottlingService.StartRun(stock, batch, Cologne, Percent(3m), Grams(100m), Settings, 0, Spirit, ready).Value;
        var bottled = BottlingService.StartRun(stock, batch, Cologne, Percent(3m), Grams(100m), Settings, 1, Spirit, ready).Value;
        BottlingService.Bottle(bottled, stock, Cologne, Settings, ready.AddMinutes(30));

        ExtractionRun.Restore(extraction.ToSnapshot()).Value.ToSnapshot().ShouldBe(extraction.ToSnapshot());
        Same(ConcentrateBatch.Restore(batch.ToSnapshot()).Value.ToSnapshot(), batch.ToSnapshot());
        Same(ProductRun.Restore(macerating.ToSnapshot()).Value.ToSnapshot(), macerating.ToSnapshot());
        Same(ProductRun.Restore(bottled.ToSnapshot()).Value.ToSnapshot(), bottled.ToSnapshot());

        var restoredStock = Inventory.Restore(stock.ToSnapshot()).Value;
        restoredStock.Shelf.ShouldBe(stock.Shelf);
        restoredStock.ShelfValue.ShouldBe(stock.ShelfValue);
    }

    [Fact]
    public void Restoring_a_job_re_checks_its_state()
    {
        var batch = ReadyBatch(Grams(300m), Francs(30m), out var inventory);
        inventory.Receive(Spirit, Qty(20m), Francs(60m));
        var run = BottlingService.StartRun(
            inventory, batch, Cologne, Percent(3m), Grams(100m), Settings, 0, Spirit, Start.AddMinutes(15)).Value;

        ConcentrateBatch.Restore(batch.ToSnapshot() with { RemainingGrams = 999m }).Error.ShouldBe(ProductionErrors.CorruptState);
        ConcentrateBatch.Restore(batch.ToSnapshot() with { FormulaRevision = 0 }).Error.ShouldBe(FormulaErrors.InvalidRevision);
        ProductRun.Restore(run.ToSnapshot() with { Status = ProductRunStatus.Bottled }).Error.ShouldBe(ProductionErrors.CorruptState);
        ProductRun.Restore(run.ToSnapshot() with { Concentration = 2m }).Error.ShouldBe(ProductionErrors.ConcentrationInvalid);
        ProductRun.Restore(run.ToSnapshot() with { CompletesAt = Start.AddYears(-1) }).Error.ShouldBe(ProductionErrors.TimingInvalid);
    }

    private static void Same<T>(T actual, T expected)
        where T : notnull =>
        System.Text.Json.JsonSerializer.Serialize(actual).ShouldBe(System.Text.Json.JsonSerializer.Serialize(expected));

    private static (FormulaRevisionRef Ref, Composition Composition) RoseAndBergamot()
    {
        var formula = Formula.Create("Rose and bergamot").Value;
        formula.AddLine(TestMaterials.Bergamot, TestMaterials.Parts(10m));
        formula.AddLine(TestMaterials.RoseOtto, TestMaterials.Parts(10m), TestMaterials.Percent(10m));
        return (formula.CurrentRevision, formula.Compose().Value);
    }

    /// <summary>A batch compounded at <see cref="Start"/>, with an inventory holding nothing else.</summary>
    private static ConcentrateBatch ReadyBatch(Mass mass, Money cost, out Inventory inventory)
    {
        inventory = Inventory.Create();
        var (formulaRef, composition) = RoseAndBergamot();
        return ConcentrateBatch.Compound(formulaRef, composition, mass, cost, Start, TimeSpan.FromMinutes(15)).Value;
    }

    private static decimal MassInStock(Inventory inventory) =>
        inventory.Lines.Where(line => line.Item != Flacons).Sum(line => line.Quantity.Value);
}
