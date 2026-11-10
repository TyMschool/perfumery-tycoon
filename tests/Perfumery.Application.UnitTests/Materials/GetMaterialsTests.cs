using Perfumery.Application.Materials.GetMaterials;
using Perfumery.Application.UnitTests.Fakes;
using Perfumery.Domain.Materials;
using static Perfumery.Application.UnitTests.Fakes.FakeMaterialCatalog;

namespace Perfumery.Application.UnitTests.Materials;

public class GetMaterialsTests
{
    private readonly GetMaterialsQueryHandler _handler = new(new FakeMaterialCatalog());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_whole_catalog_without_a_filter()
    {
        var result = await _handler.Handle(new GetMaterialsQuery(), Ct);

        result.Value.Select(material => material.Id).ShouldBe([BergamotId, RoseOttoId, SandalwoodId]);
    }

    [Theory]
    [InlineData(NoteTier.Top, BergamotId)]
    [InlineData(NoteTier.Heart, RoseOttoId)]
    [InlineData(NoteTier.Base, SandalwoodId)]
    public async Task Filters_by_note_tier(NoteTier tier, string expectedId)
    {
        var result = await _handler.Handle(new GetMaterialsQuery(tier), Ct);

        var material = result.Value.ShouldHaveSingleItem();
        material.Id.ShouldBe(expectedId);
        material.NoteTier.ShouldBe(tier.ToString());
    }

    [Fact]
    public async Task Maps_every_field_of_a_material()
    {
        var result = await _handler.Handle(new GetMaterialsQuery(NoteTier.Heart), Ct);

        var rose = result.Value.Single();
        rose.Name.ShouldBe("Rose otto");
        rose.BotanicalSource.ShouldBe("Test botanical");
        rose.Origin.ShouldBe("Test origin");
        rose.ExtractionMethod.ShouldBe("SteamDistillation");
        rose.Facets["Floral"].ShouldBe(1.0);
        rose.Facets["Citrus"].ShouldBe(0d);
        rose.Facets.Count.ShouldBe(10);
        rose.OdorStrength.ShouldBe(4.0);
        rose.HalfLifeHours.ShouldBe(6.0);
        rose.CostPerKg.ShouldBe(2400m);
        rose.StockDilutionPercent.ShouldBe(10m);
    }
}
