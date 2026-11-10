using System.Text;
using Perfumery.Domain.Materials;
using Perfumery.Infrastructure.Materials;

namespace Perfumery.Infrastructure.UnitTests;

public class JsonMaterialCatalogTests
{
    private readonly JsonMaterialCatalog _catalog = new();

    [Fact]
    public void All_15_era_1_materials_load()
    {
        _catalog.GetAll().Select(material => material.Id.Value).ShouldBe(
        [
            "bergamot-calabria",
            "lemon-sicily",
            "sweet-orange-sicily",
            "neroli-vallauris",
            "petitgrain-bigarade",
            "rosemary-dalmatia",
            "lavender-haute-provence",
            "rose-otto-kazanlak",
            "jasmine-enfleurage-grasse",
            "orris-butter-tuscany",
            "clove-bud-zanzibar",
            "vetiver-reunion",
            "sandalwood-mysore",
            "benzoin-tincture-siam",
            "tonka-bean-tincture-venezuela",
        ]);
    }

    [Fact]
    public void Every_material_is_fully_described()
    {
        foreach (var material in _catalog.GetAll())
        {
            material.Name.ShouldNotBeNullOrWhiteSpace();
            material.BotanicalSource.ShouldNotBeNullOrWhiteSpace();
            material.Origin.ShouldNotBeNullOrWhiteSpace();
            material.Facets.Dominant.ShouldNotBeNull();
            material.CostPerKg.Amount.ShouldBeGreaterThan(0m);
        }
    }

    [Fact]
    public void Find_returns_a_material_by_id_or_null()
    {
        var bergamot = _catalog.Find(Id("bergamot-calabria"));

        bergamot.ShouldNotBeNull();
        bergamot.Name.ShouldBe("Bergamot oil");
        bergamot.Origin.ShouldBe("Calabria");
        bergamot.ExtractionMethod.ShouldBe(ExtractionMethod.ColdExpression);
        _catalog.Find(Id("oakmoss-yugoslavia")).ShouldBeNull();
    }

    [Theory]
    [InlineData("bergamot-calabria", ExtractionMethod.ColdExpression, NoteTier.Top, Facet.Citrus, 100)]
    [InlineData("lemon-sicily", ExtractionMethod.ColdExpression, NoteTier.Top, Facet.Citrus, 100)]
    [InlineData("sweet-orange-sicily", ExtractionMethod.ColdExpression, NoteTier.Top, Facet.Citrus, 100)]
    [InlineData("neroli-vallauris", ExtractionMethod.SteamDistillation, NoteTier.Heart, Facet.Floral, 10)]
    [InlineData("petitgrain-bigarade", ExtractionMethod.SteamDistillation, NoteTier.Top, Facet.Green, 100)]
    [InlineData("rosemary-dalmatia", ExtractionMethod.SteamDistillation, NoteTier.Top, Facet.Aromatic, 100)]
    [InlineData("lavender-haute-provence", ExtractionMethod.SteamDistillation, NoteTier.Top, Facet.Aromatic, 100)]
    [InlineData("rose-otto-kazanlak", ExtractionMethod.SteamDistillation, NoteTier.Heart, Facet.Floral, 10)]
    [InlineData("jasmine-enfleurage-grasse", ExtractionMethod.Enfleurage, NoteTier.Heart, Facet.Floral, 10)]
    [InlineData("orris-butter-tuscany", ExtractionMethod.SteamDistillation, NoteTier.Base, Facet.Powdery, 10)]
    [InlineData("clove-bud-zanzibar", ExtractionMethod.SteamDistillation, NoteTier.Heart, Facet.Spicy, 10)]
    [InlineData("vetiver-reunion", ExtractionMethod.SteamDistillation, NoteTier.Base, Facet.Woody, 100)]
    [InlineData("sandalwood-mysore", ExtractionMethod.SteamDistillation, NoteTier.Base, Facet.Woody, 100)]
    [InlineData("benzoin-tincture-siam", ExtractionMethod.Tincture, NoteTier.Base, Facet.Balsamic, 100)]
    [InlineData("tonka-bean-tincture-venezuela", ExtractionMethod.Tincture, NoteTier.Base, Facet.Balsamic, 100)]
    public void Materials_match_the_design_table(
        string id,
        ExtractionMethod method,
        NoteTier tier,
        Facet mainFacet,
        int stockPercent)
    {
        var material = _catalog.Find(Id(id)).ShouldNotBeNull();

        material.ExtractionMethod.ShouldBe(method);
        material.Tier.ShouldBe(tier);
        material.Facets.Dominant.ShouldBe(mainFacet);
        material.StockDilution.Percent.ShouldBe(stockPercent);
    }

    [Fact]
    public void Citrus_oils_fade_within_a_couple_of_hours()
    {
        foreach (var id in new[] { "bergamot-calabria", "lemon-sicily", "sweet-orange-sicily" })
        {
            Get(id).Volatility.HalfLifeHours.ShouldBeLessThanOrEqualTo(1d);
        }
    }

    [Fact]
    public void Sandalwood_and_vetiver_last_days()
    {
        Get("sandalwood-mysore").Volatility.HalfLifeHours.ShouldBeGreaterThanOrEqualTo(48d);
        Get("vetiver-reunion").Volatility.HalfLifeHours.ShouldBeGreaterThanOrEqualTo(48d);
    }

    [Fact]
    public void Rose_otto_and_orris_cost_far_more_than_citrus()
    {
        var dearestCitrus = new[] { "bergamot-calabria", "lemon-sicily", "sweet-orange-sicily" }
            .Max(id => Get(id).CostPerKg.Amount);

        Get("rose-otto-kazanlak").CostPerKg.Amount.ShouldBeGreaterThan(dearestCitrus * 20m);
        Get("orris-butter-tuscany").CostPerKg.Amount.ShouldBeGreaterThan(dearestCitrus * 20m);
    }

    [Fact]
    public void Tinctures_are_weak_because_the_tincture_itself_is_what_is_added()
    {
        var weakestOil = _catalog.GetAll()
            .Where(material => material.ExtractionMethod != ExtractionMethod.Tincture)
            .Min(material => material.OdorStrength);

        Get("benzoin-tincture-siam").OdorStrength.ShouldBeLessThan(weakestOil);
        Get("tonka-bean-tincture-venezuela").OdorStrength.ShouldBeLessThan(weakestOil);
    }

    [Theory]
    [InlineData("""[{"id":"Bad Id","name":"X","botanicalSource":"b","origin":"o","extractionMethod":"tincture","facets":{},"odorStrength":1,"halfLifeHours":1,"costPerKg":1,"stockDilutionPercent":100}]""")]
    [InlineData("""[{"id":"x","name":"X","botanicalSource":"b","origin":"o","extractionMethod":"tincture","facets":{"citrus":1.5},"odorStrength":1,"halfLifeHours":1,"costPerKg":1,"stockDilutionPercent":100}]""")]
    [InlineData("""[{"id":"x","name":"X","botanicalSource":"b","origin":"o","extractionMethod":"tincture","facets":{},"odorStrength":1,"halfLifeHours":0,"costPerKg":1,"stockDilutionPercent":100}]""")]
    [InlineData("""[{"id":"x","name":"X","botanicalSource":"b","origin":"o","extractionMethod":"tincture","facets":{},"odorStrength":1,"halfLifeHours":1,"costPerKg":1,"stockDilutionPercent":0}]""")]
    public void Invalid_reference_data_is_a_bug_and_throws(string json)
    {
        Should.Throw<InvalidOperationException>(() => Load(json));
    }

    [Fact]
    public void A_duplicate_material_id_throws()
    {
        const string entry = """{"id":"x","name":"X","botanicalSource":"b","origin":"o","extractionMethod":"tincture","facets":{},"odorStrength":1,"halfLifeHours":1,"costPerKg":1,"stockDilutionPercent":100}""";

        Should.Throw<InvalidOperationException>(() => Load($"[{entry},{entry}]"));
        Load($"[{entry}]").GetAll().ShouldHaveSingleItem();
    }

    private static JsonMaterialCatalog Load(string json) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(json)), "test.json");

    private Material Get(string id) => _catalog.Find(Id(id)).ShouldNotBeNull();

    private static MaterialId Id(string value) => MaterialId.Create(value).Value;
}
