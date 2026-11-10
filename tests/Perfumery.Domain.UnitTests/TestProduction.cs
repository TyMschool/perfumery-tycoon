using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;

namespace Perfumery.Domain.UnitTests;

/// <summary>Hand-built era 1 production reference data, so domain tests need no JSON.</summary>
internal static class TestProduction
{
    public static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public static readonly SupplyId SpiritId = SupplyId.Create("spirit-of-wine").Value;
    public static readonly SupplyId LavenderTopsId = SupplyId.Create("lavender-flowering-tops").Value;
    public static readonly SupplyId BenzoinResinId = SupplyId.Create("benzoin-resin").Value;
    public static readonly SupplyId FlaconId = SupplyId.Create("flacon-cologne-200g").Value;

    public static readonly StockItemId Spirit = StockItemId.For(SpiritId);
    public static readonly StockItemId LavenderTops = StockItemId.For(LavenderTopsId);
    public static readonly StockItemId BenzoinResin = StockItemId.For(BenzoinResinId);
    public static readonly StockItemId Flacons = StockItemId.For(FlaconId);

    public static readonly MaterialId LavenderOil = MaterialId.Create("lavender-haute-provence").Value;
    public static readonly MaterialId BenzoinTincture = MaterialId.Create("benzoin-tincture-siam").Value;

    /// <summary>10 g of oil per kg of tops, 20 minutes in the still.</summary>
    public static readonly ExtractionRecipe DistilLavender = ExtractionRecipe.Create(
        ExtractionRecipeId.Create("distil-lavender").Value,
        LavenderOil,
        ExtractionMethod.SteamDistillation,
        LavenderTopsId,
        0m,
        Yield.Create(0.010m).Value,
        TimeSpan.FromMinutes(20),
        EquipmentKind.Still).Value;

    /// <summary>1 kg of resin plus 5 kg of spirit gives 5 kg of tincture after 3 hours.</summary>
    public static readonly ExtractionRecipe TinctureBenzoin = ExtractionRecipe.Create(
        ExtractionRecipeId.Create("tincture-benzoin").Value,
        BenzoinTincture,
        ExtractionMethod.Tincture,
        BenzoinResinId,
        5m,
        Yield.Create(5m).Value,
        TimeSpan.FromHours(3),
        EquipmentKind.TinctureJar).Value;

    /// <summary>2 to 5%, default 3%, 30 minutes, 200 g flacons.</summary>
    public static readonly ProductType Cologne = ProductType.Create(
        ProductTypeId.Create("eau-de-cologne").Value,
        "Eau de Cologne",
        Percent(2m),
        Percent(5m),
        Percent(3m),
        TimeSpan.FromMinutes(30),
        FlaconId,
        Grams(200m)).Value;

    public static readonly AtelierSettings Settings = SettingsWith();

    public static AtelierSettings SettingsWith(decimal timeScale = 1m, decimal filterLoss = 0.02m) =>
        AtelierSettings.Create(
            Francs(5000m),
            new Dictionary<EquipmentKind, EquipmentSpec>
            {
                [EquipmentKind.Still] = new(1, Kilograms(100m)),
                [EquipmentKind.TinctureJar] = new(2, Kilograms(10m)),
                [EquipmentKind.MacerationVessel] = new(3, Kilograms(20m)),
            },
            filterLoss,
            timeScale,
            TimeSpan.FromMinutes(15)).Value;

    public static Mass Grams(decimal grams) => Mass.FromGrams(grams).Value;

    public static Mass Kilograms(decimal kilograms) => Mass.FromKilograms(kilograms).Value;

    public static Money Francs(decimal amount) => Money.Create(amount).Value;

    public static Quantity Qty(decimal value) => Quantity.Create(value).Value;

    public static Concentration Percent(decimal percent) => Concentration.FromPercent(percent).Value;

    /// <summary>An inventory holding the given items, each bought at the given price per unit.</summary>
    public static Inventory Stocked(params (StockItemId Item, decimal Quantity, decimal UnitPrice)[] lines)
    {
        var inventory = Inventory.Create();
        foreach (var (item, quantity, unitPrice) in lines)
        {
            inventory.Receive(item, Qty(quantity), Francs(quantity * unitPrice));
        }

        return inventory;
    }
}
