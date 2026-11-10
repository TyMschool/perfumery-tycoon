using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using static Perfumery.Domain.UnitTests.TestProduction;

namespace Perfumery.Domain.UnitTests.Ateliers;

public class AtelierTests
{
    [Fact]
    public void Opening_records_the_starting_capital()
    {
        var atelier = Atelier.Open(Francs(5000m), Start);

        atelier.Cash.Amount.ShouldBe(5000m);
        atelier.LastSeen.ShouldBe(Start);
        atelier.Ledger.ShouldHaveSingleItem()
            .ShouldBe(new LedgerEntry(Start, 5000m, LedgerCategory.StartingCapital, "Starting capital"));
    }

    [Fact]
    public void Spending_writes_a_negative_ledger_entry_rounded_to_the_centime()
    {
        var atelier = Atelier.Open(Francs(100m), Start);

        atelier.Spend(Francs(12.345m), LedgerCategory.SuppliesPurchased, "spirit", Start).IsSuccess.ShouldBeTrue();

        atelier.Cash.Amount.ShouldBe(87.65m);
        atelier.Ledger[^1].Amount.ShouldBe(-12.35m);
    }

    [Fact]
    public void Spending_more_than_the_cash_fails_and_changes_nothing()
    {
        var atelier = Atelier.Open(Francs(100m), Start);

        var result = atelier.Spend(Francs(100.01m), LedgerCategory.SuppliesPurchased, "rose otto", Start);

        result.Error.Code.ShouldBe("Atelier.InsufficientFunds");
        atelier.Cash.Amount.ShouldBe(100m);
        atelier.Ledger.Count.ShouldBe(1);
    }

    [Fact]
    public void Spending_exactly_the_cash_is_allowed()
    {
        var atelier = Atelier.Open(Francs(100m), Start);

        atelier.Spend(Francs(100m), LedgerCategory.SuppliesPurchased, "everything", Start).IsSuccess.ShouldBeTrue();

        atelier.Cash.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Cash_equals_the_ledger_total_after_any_sequence_including_carry_forward()
    {
        var random = new Random(1851);
        var atelier = Atelier.Open(Francs(5000m), Start);

        for (var i = 0; i < Atelier.MaxLedgerEntries * 3; i++)
        {
            var amount = Francs(random.Next(1, 2000) / 100m);
            if (random.Next(2) == 0)
            {
                atelier.Spend(amount, LedgerCategory.SuppliesPurchased, "purchase", Start.AddMinutes(i));
            }
            else
            {
                atelier.Receive(amount, LedgerCategory.StartingCapital, "gift", Start.AddMinutes(i));
            }

            (atelier.CarriedForward.Amount + atelier.Ledger.Sum(entry => entry.Amount)).ShouldBe(atelier.Cash.Amount);
        }

        atelier.Ledger.Count.ShouldBe(Atelier.MaxLedgerEntries);
        atelier.CarriedForward.Amount.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void The_clock_never_runs_backwards()
    {
        var atelier = Atelier.Open(Francs(100m), Start);

        atelier.Observe(Start.AddHours(2)).ShouldBe(Start.AddHours(2));
        atelier.Observe(Start.AddHours(1)).ShouldBe(Start.AddHours(2));
        atelier.EffectiveNow(Start).ShouldBe(Start.AddHours(2));
        atelier.EffectiveNow(Start.AddHours(3)).ShouldBe(Start.AddHours(3));
        atelier.LastSeen.ShouldBe(Start.AddHours(2));
    }

    [Fact]
    public void The_atelier_survives_a_snapshot_round_trip()
    {
        var atelier = Atelier.Open(Francs(5000m), Start);
        atelier.Spend(Francs(12.5m), LedgerCategory.SuppliesPurchased, "lavender", Start.AddMinutes(5));
        atelier.Observe(Start.AddHours(1));

        var restored = Atelier.Restore(atelier.ToSnapshot()).Value;

        restored.Id.ShouldBe(atelier.Id);
        restored.Cash.ShouldBe(atelier.Cash);
        restored.LastSeen.ShouldBe(atelier.LastSeen);
        restored.Ledger.ShouldBe(atelier.Ledger);
    }

    [Fact]
    public void Restore_rejects_cash_that_does_not_match_the_ledger()
    {
        var snapshot = Atelier.Open(Francs(5000m), Start).ToSnapshot();

        Atelier.Restore(snapshot with { Cash = 9999m }).Error.ShouldBe(AtelierErrors.LedgerMismatch);
        Atelier.Restore(snapshot with { Cash = -1m }).Error.ShouldBe(MoneyErrors.Negative);
    }

    [Fact]
    public void Settings_reject_invalid_tuning()
    {
        var equipment = Settings.Equipment;

        AtelierSettings.Create(Francs(1m), equipment, 1m, 1m, TimeSpan.Zero).Error.ShouldBe(AtelierErrors.InvalidFilterLoss);
        AtelierSettings.Create(Francs(1m), equipment, 0m, 0m, TimeSpan.Zero).Error.ShouldBe(AtelierErrors.InvalidTimeScale);
        AtelierSettings.Create(Francs(1m), equipment, 0m, 1m, TimeSpan.FromMinutes(-1)).Error.ShouldBe(AtelierErrors.InvalidDuration);
        AtelierSettings.Create(Francs(1m), new Dictionary<EquipmentKind, EquipmentSpec>(), 0m, 1m, TimeSpan.Zero)
            .Error.ShouldBe(AtelierErrors.InvalidEquipment);
    }

    [Fact]
    public void The_time_scale_divides_durations()
    {
        SettingsWith(timeScale: 60m).Scale(TimeSpan.FromHours(3)).ShouldBe(TimeSpan.FromMinutes(3));
        Settings.Scale(TimeSpan.FromHours(3)).ShouldBe(TimeSpan.FromHours(3));
    }

    [Fact]
    public void Equipment_is_free_until_every_unit_is_in_use()
    {
        Settings.CheckFree(EquipmentKind.TinctureJar, 1).IsSuccess.ShouldBeTrue();
        Settings.CheckFree(EquipmentKind.TinctureJar, 2).Error.Code.ShouldBe("Atelier.NoFreeEquipment");
        Settings.CheckFree(EquipmentKind.Still, 1).Error.Code.ShouldBe("Atelier.NoFreeEquipment");
    }
}
