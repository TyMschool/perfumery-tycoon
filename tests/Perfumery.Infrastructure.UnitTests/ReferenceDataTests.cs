using System.Text;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.Infrastructure.Ateliers;
using Perfumery.Infrastructure.Materials;
using Perfumery.Infrastructure.Production;
using Perfumery.Infrastructure.Stock;

namespace Perfumery.Infrastructure.UnitTests;

public class ReferenceDataTests
{
    private readonly JsonMaterialCatalog _materials = new();
    private readonly JsonSupplyCatalog _supplies = new();
    private readonly JsonExtractionRecipeCatalog _recipes;
    private readonly JsonProductTypeCatalog _productTypes;

    public ReferenceDataTests()
    {
        _recipes = new JsonExtractionRecipeCatalog(_materials, _supplies);
        _productTypes = new JsonProductTypeCatalog(_supplies);
    }

    [Fact]
    public void Supplies_load_with_exactly_one_spirit()
    {
        _supplies.GetAll().Count.ShouldBe(10);
        _supplies.Spirit.Id.Value.ShouldBe("spirit-of-wine");
        _supplies.Spirit.Unit.ShouldBe(StockUnit.Kilogram);
        _supplies.GetAll().Count(supply => supply.Kind == SupplyKind.RawBotanical).ShouldBe(6);
        _supplies.GetAll().Where(supply => supply.Kind == SupplyKind.Packaging).ShouldAllBe(supply => supply.Unit == StockUnit.Each);
        _supplies.Find(SupplyId.Create("moon-dust").Value).ShouldBeNull();
    }

    [Theory]
    [InlineData("distil-lavender", "lavender-haute-provence", EquipmentKind.Still, 20, 0.010, 0)]
    [InlineData("distil-rosemary", "rosemary-dalmatia", EquipmentKind.Still, 20, 0.010, 0)]
    [InlineData("distil-petitgrain", "petitgrain-bigarade", EquipmentKind.Still, 20, 0.003, 0)]
    [InlineData("distil-neroli", "neroli-vallauris", EquipmentKind.Still, 20, 0.001, 0)]
    [InlineData("tincture-benzoin", "benzoin-tincture-siam", EquipmentKind.TinctureJar, 180, 5, 5)]
    [InlineData("tincture-tonka", "tonka-bean-tincture-venezuela", EquipmentKind.TinctureJar, 180, 8, 8)]
    public void Extraction_recipes_match_the_design_table(
        string id,
        string output,
        EquipmentKind equipment,
        int minutes,
        double yield,
        double spiritPerKgRaw)
    {
        var recipe = _recipes.Find(ExtractionRecipeId.Create(id).Value).ShouldNotBeNull();

        recipe.Output.Value.ShouldBe(output);
        recipe.Equipment.ShouldBe(equipment);
        recipe.Duration.ShouldBe(TimeSpan.FromMinutes(minutes));
        recipe.Yield.KgPerKgRaw.ShouldBe((decimal)yield);
        recipe.SpiritPerKgRaw.ShouldBe((decimal)spiritPerKgRaw);
        _recipes.GetAll().Count.ShouldBe(6);
    }

    [Fact]
    public void Making_a_material_costs_less_than_buying_it_but_not_by_much()
    {
        foreach (var recipe in _recipes.GetAll())
        {
            var raw = _supplies.Find(recipe.RawBotanical).ShouldNotBeNull();
            var costPerKg = (raw.Price.Amount + (recipe.SpiritPerKgRaw * _supplies.Spirit.Price.Amount)) / recipe.Yield.KgPerKgRaw;
            var price = _materials.Find(recipe.Output).ShouldNotBeNull().CostPerKg.Amount;

            costPerKg.ShouldBeLessThan(price, recipe.Id.Value);
            costPerKg.ShouldBeGreaterThan(price * 0.5m, recipe.Id.Value);
        }
    }

    [Fact]
    public void A_full_tincture_recipe_charge_fits_the_jar_only_as_raw_plus_spirit()
    {
        var settings = AtelierSettingsLoader.Load();
        var jar = settings.Equipment[EquipmentKind.TinctureJar].Capacity;

        foreach (var recipe in _recipes.GetAll().Where(recipe => recipe.Equipment == EquipmentKind.TinctureJar))
        {
            var oneKilogram = Domain.Common.Mass.FromKilograms(1m).Value;
            ExtractionRun.Check(recipe, oneKilogram, jar).IsSuccess.ShouldBeTrue(recipe.Id.Value);
            ExtractionRun.Check(recipe, jar, jar).Error.Code.ShouldBe("Production.OverCapacity");
        }
    }

    [Theory]
    [InlineData("eau-de-cologne", 2, 5, 3, 30, 200)]
    [InlineData("toilet-water", 5, 10, 7, 60, 100)]
    [InlineData("extrait", 15, 30, 20, 180, 30)]
    public void Product_types_match_the_design_table(string id, int min, int max, int @default, int minutes, int fillGrams)
    {
        var productType = _productTypes.Find(ProductTypeId.Create(id).Value).ShouldNotBeNull();

        productType.MinConcentration.Percent.ShouldBe(min);
        productType.MaxConcentration.Percent.ShouldBe(max);
        productType.DefaultConcentration.Percent.ShouldBe(@default);
        productType.Maceration.ShouldBe(TimeSpan.FromMinutes(minutes));
        productType.FlaconFill.Grams.ShouldBe(fillGrams);
        _productTypes.GetAll().Count.ShouldBe(3);
    }

    [Fact]
    public void Atelier_settings_load_and_the_time_scale_can_be_overridden()
    {
        var settings = AtelierSettingsLoader.Load();

        settings.StartingCapital.Amount.ShouldBe(5000m);
        settings.FilterLoss.ShouldBe(0.02m);
        settings.TimeScale.ShouldBe(1m);
        settings.ConcentrateMaturation.ShouldBe(TimeSpan.FromMinutes(15));
        settings.Equipment[EquipmentKind.Still].ShouldBe(new EquipmentSpec(1, Domain.Common.Mass.FromKilograms(100m).Value));
        settings.Equipment[EquipmentKind.TinctureJar].Count.ShouldBe(2);
        settings.Equipment[EquipmentKind.MacerationVessel].Count.ShouldBe(3);
        AtelierSettingsLoader.Load(60m).TimeScale.ShouldBe(60m);
    }

    [Fact]
    public void Invalid_reference_data_is_a_bug_and_throws()
    {
        const string noSpirit = """[{"id":"flacon","name":"Flacon","kind":"packaging","unit":"each","price":1}]""";
        const string twoSpirits = """[{"id":"a","name":"A","kind":"solvent","unit":"kilogram","price":1},{"id":"b","name":"B","kind":"solvent","unit":"kilogram","price":1}]""";
        const string unknownOutput = """[{"id":"distil-x","output":"oakmoss","method":"steamDistillation","rawBotanical":"lavender-flowering-tops","spiritPerKgRaw":0,"yieldKgPerKgRaw":0.01,"durationMinutes":20,"equipment":"still"}]""";
        const string flaconIsSpirit = """[{"id":"x","name":"X","minConcentrationPercent":2,"maxConcentrationPercent":5,"defaultConcentrationPercent":3,"macerationMinutes":30,"flacon":"spirit-of-wine","flaconFillGrams":200}]""";
        const string badSettings = """{"startingCapital":5000,"filterLoss":1.5,"timeScale":1,"concentrateMaturationMinutes":15,"equipment":{"still":{"count":1,"capacityKg":100},"tinctureJar":{"count":2,"capacityKg":10},"macerationVessel":{"count":3,"capacityKg":20}}}""";

        Should.Throw<InvalidOperationException>(() => new JsonSupplyCatalog(Stream(noSpirit), "test.json"));
        Should.Throw<InvalidOperationException>(() => new JsonSupplyCatalog(Stream(twoSpirits), "test.json"));
        Should.Throw<InvalidOperationException>(() => new JsonExtractionRecipeCatalog(Stream(unknownOutput), "test.json", _materials, _supplies));
        Should.Throw<InvalidOperationException>(() => new JsonProductTypeCatalog(Stream(flaconIsSpirit), "test.json", _supplies));
        Should.Throw<InvalidOperationException>(() => AtelierSettingsLoader.Load(Stream(badSettings), "test.json"));
    }

    private static MemoryStream Stream(string json) => new(Encoding.UTF8.GetBytes(json));
}
