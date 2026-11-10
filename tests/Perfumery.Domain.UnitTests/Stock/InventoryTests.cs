using Perfumery.Domain.Common;
using Perfumery.Domain.Stock;
using static Perfumery.Domain.UnitTests.TestProduction;

namespace Perfumery.Domain.UnitTests.Stock;

public class InventoryTests
{
    [Fact]
    public void New_value_objects_reject_out_of_range_input()
    {
        Mass.FromGrams(-0.01m).Error.ShouldBe(MassErrors.Negative);
        Mass.FromGrams(1.005m).Value.Grams.ShouldBe(1.01m);
        Mass.FromKilograms(1.5m).Value.Grams.ShouldBe(1500m);
        Quantity.Create(-1m).Error.ShouldBe(StockErrors.QuantityNegative);
        Quantity.Create(2.5m).Value.IsWhole.ShouldBeFalse();
        SupplyId.Create("Spirit Of Wine").Error.ShouldBe(SupplyErrors.IdMalformed);
        Supply.Create(SpiritId, " ", SupplyKind.Solvent, StockUnit.Kilogram, Francs(3m)).Error.ShouldBe(SupplyErrors.NameRequired);
    }

    [Theory]
    [InlineData("material:lavender-haute-provence", StockItemKind.Material, "lavender-haute-provence")]
    [InlineData("supply:spirit-of-wine", StockItemKind.Supply, "spirit-of-wine")]
    public void Stock_item_ids_parse_and_print_as_kind_and_slug(string text, StockItemKind kind, string slug)
    {
        var item = StockItemId.Parse(text).Value;

        item.Kind.ShouldBe(kind);
        item.Slug.ShouldBe(slug);
        item.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("lavender")]
    [InlineData("perfume:lavender")]
    [InlineData("material:Lavender Oil")]
    [InlineData("material:a:b")]
    public void Malformed_stock_item_ids_are_rejected(string? text)
    {
        StockItemId.Parse(text).Error.ShouldBe(StockErrors.ItemIdMalformed);
    }

    [Fact]
    public void Receiving_twice_gives_the_weighted_average_cost()
    {
        // 10 kg at 3.00 then 30 kg at 4.00: (30 + 120) ÷ 40 = 3.75.
        var inventory = Stocked((Spirit, 10m, 3m), (Spirit, 30m, 4m));

        inventory.QuantityOf(Spirit).Value.ShouldBe(40m);
        inventory.AverageUnitCost(Spirit).Amount.ShouldBe(3.75m);
        inventory.StockValue.Amount.ShouldBe(150m);
    }

    [Fact]
    public void Receiving_nothing_is_rejected()
    {
        Inventory.Create().Receive(Spirit, Quantity.Zero, Money.Zero).Error.ShouldBe(StockErrors.QuantityZero);
    }

    [Fact]
    public void Consuming_takes_stock_out_at_the_average_and_leaves_the_average_unchanged()
    {
        var inventory = Stocked((Spirit, 10m, 3m), (Spirit, 30m, 4m));

        var consumed = inventory.Reserve([new StockRequirement(Spirit, Qty(8m))]).Value;

        consumed.TotalCost.Amount.ShouldBe(30m);
        inventory.QuantityOf(Spirit).Value.ShouldBe(32m);
        inventory.AverageUnitCost(Spirit).Amount.ShouldBe(3.75m);
    }

    [Fact]
    public void Consuming_everything_removes_the_line_and_takes_its_whole_cost()
    {
        var inventory = Stocked((Spirit, 3m, 1m / 3m));

        var consumed = inventory.Reserve([new StockRequirement(Spirit, Qty(3m))]).Value;

        consumed.TotalCost.Amount.ShouldBe(3m * (1m / 3m));
        inventory.Lines.ShouldBeEmpty();
        inventory.StockValue.ShouldBe(Money.Zero);
    }

    [Fact]
    public void A_reservation_with_a_shortage_consumes_nothing_and_reports_every_shortage()
    {
        var inventory = Stocked((Spirit, 10m, 3m), (LavenderTops, 5m, 0.25m));

        var result = inventory.Reserve(
        [
            new StockRequirement(Spirit, Qty(4m)),
            new StockRequirement(LavenderTops, Qty(8m)),
            new StockRequirement(Flacons, Qty(49m)),
        ]);

        var error = result.Error.ShouldBeOfType<InsufficientStockError>();
        error.Shortages.Select(shortage => (shortage.Item, shortage.Missing))
            .ShouldBe([(LavenderTops, 3m), (Flacons, 49m)], ignoreOrder: true);
        inventory.QuantityOf(Spirit).Value.ShouldBe(10m);
        inventory.QuantityOf(LavenderTops).Value.ShouldBe(5m);
    }

    [Fact]
    public void Requirements_for_the_same_item_are_added_together()
    {
        var inventory = Stocked((Spirit, 10m, 3m));
        StockRequirement[] twice = [new(Spirit, Qty(6m)), new(Spirit, Qty(6m))];

        inventory.Shortages(twice).ShouldHaveSingleItem().Missing.ShouldBe(2m);
        inventory.Reserve(twice).IsFailure.ShouldBeTrue();
        inventory.QuantityOf(Spirit).Value.ShouldBe(10m);
    }

    [Fact]
    public void Cost_is_conserved_over_any_sequence_of_receipts_and_reservations()
    {
        var random = new Random(1851);
        for (var round = 0; round < 200; round++)
        {
            var inventory = Inventory.Create();
            var paid = 0m;
            var taken = 0m;

            for (var step = 0; step < 20; step++)
            {
                if (random.Next(3) > 0 || inventory.QuantityOf(Spirit).IsZero)
                {
                    var quantity = random.Next(1, 500_000) / 1000m;
                    var cost = Math.Round(quantity * random.Next(1, 9000) / 1000m, 2);
                    inventory.Receive(Spirit, Qty(quantity), Francs(cost));
                    paid += cost;
                }
                else
                {
                    var held = inventory.QuantityOf(Spirit).Value;
                    var quantity = Math.Min(held, Math.Round(held * random.Next(1, 101) / 100m, 5));
                    taken += inventory.Reserve([new StockRequirement(Spirit, Qty(quantity))]).Value.TotalCost.Amount;
                }

                inventory.QuantityOf(Spirit).Value.ShouldBeGreaterThanOrEqualTo(0m);
            }

            (taken + inventory.StockValue.Amount).ShouldBe(paid);
        }
    }

    [Fact]
    public void The_inventory_survives_a_snapshot_round_trip()
    {
        var inventory = Stocked((Spirit, 10m, 3m), (Flacons, 49m, 0.4m));

        var restored = Inventory.Restore(inventory.ToSnapshot()).Value;

        restored.Id.ShouldBe(inventory.Id);
        restored.Lines.ShouldBe(inventory.Lines);
    }

    [Fact]
    public void Restore_rejects_corrupt_stock_lines()
    {
        var snapshot = Inventory.Create().ToSnapshot();
        var line = new StockLineSnapshot("supply:spirit-of-wine", 10m, 30m);

        Inventory.Restore(snapshot with { Lines = [line, line] }).Error.ShouldBe(StockErrors.CorruptLine);
        Inventory.Restore(snapshot with { Lines = [line with { Quantity = 0m }] }).Error.ShouldBe(StockErrors.CorruptLine);
        Inventory.Restore(snapshot with { Lines = [line with { Quantity = -1m }] }).Error.ShouldBe(StockErrors.QuantityNegative);
        Inventory.Restore(snapshot with { Lines = [line with { Item = "nonsense" }] }).Error.ShouldBe(StockErrors.ItemIdMalformed);
    }
}
