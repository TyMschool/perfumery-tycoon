using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;

namespace Perfumery.Domain.UnitTests.Materials;

public class ValueObjectTests
{
    [Theory]
    [InlineData("bergamot-calabria")]
    [InlineData("vetiver")]
    [InlineData("no-5-base")]
    public void MaterialId_accepts_lowercase_hyphenated_slugs(string value)
    {
        MaterialId.Create(value).Value.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaterialId_rejects_missing_values(string? value)
    {
        MaterialId.Create(value).Error.ShouldBe(MaterialErrors.IdRequired);
    }

    [Theory]
    [InlineData("Bergamot")]
    [InlineData("bergamot calabria")]
    [InlineData("bergamot_calabria")]
    [InlineData("-bergamot")]
    [InlineData("bergamot-")]
    [InlineData("bergamot--calabria")]
    [InlineData("réunion")]
    public void MaterialId_rejects_malformed_slugs(string value)
    {
        MaterialId.Create(value).Error.ShouldBe(MaterialErrors.IdMalformed);
    }

    [Fact]
    public void MaterialIds_with_the_same_slug_are_equal()
    {
        MaterialId.Create("vetiver").Value.ShouldBe(MaterialId.Create("vetiver").Value);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(1000)]
    public void Parts_accepts_the_slider_range(double value)
    {
        Parts.Create((decimal)value).Value.Value.ShouldBe((decimal)value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.009)]
    [InlineData(-1)]
    [InlineData(1000.01)]
    public void Parts_rejects_values_outside_the_range(double value)
    {
        Parts.Create((decimal)value).Error.ShouldBe(PartsErrors.OutOfRange);
    }

    [Theory]
    [InlineData(0.1, 0.001)]
    [InlineData(10, 0.1)]
    [InlineData(100, 1)]
    public void Dilution_converts_percent_to_fraction(double percent, double fraction)
    {
        Dilution.FromPercent((decimal)percent).Value.Fraction.ShouldBe((decimal)fraction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.1)]
    public void Dilution_rejects_values_outside_the_range(double percent)
    {
        Dilution.FromPercent((decimal)percent).Error.ShouldBe(DilutionErrors.OutOfRange);
        Dilution.FromFraction((decimal)percent / 100m).Error.ShouldBe(DilutionErrors.OutOfRange);
    }

    [Fact]
    public void Dilution_at_100_percent_is_neat()
    {
        Dilution.FromPercent(100m).Value.ShouldBe(Dilution.Neat);
        Dilution.Neat.IsNeat.ShouldBeTrue();
        Dilution.FromPercent(10m).Value.IsNeat.ShouldBeFalse();
    }

    [Fact]
    public void Money_rejects_negative_amounts()
    {
        Money.Create(-0.01m).Error.ShouldBe(MoneyErrors.Negative);
        Money.Create(0m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Money_adds_and_scales()
    {
        var total = Money.Create(40m).Value + Money.Create(2.5m).Value;

        total.Amount.ShouldBe(42.5m);
        total.Times(0.5m).Amount.ShouldBe(21.25m);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Facets_rejects_weights_outside_0_to_1(double weight)
    {
        Facets.Create([KeyValuePair.Create(Facet.Citrus, weight)]).Error.ShouldBe(FacetErrors.WeightOutOfRange);
    }

    [Fact]
    public void Facets_rejects_an_undefined_facet()
    {
        Facets.Create([KeyValuePair.Create((Facet)99, 0.5)]).Error.ShouldBe(FacetErrors.UnknownFacet);
    }

    [Fact]
    public void Facets_left_out_are_zero_and_the_strongest_is_dominant()
    {
        var facets = Facets.Create([KeyValuePair.Create(Facet.Woody, 0.8), KeyValuePair.Create(Facet.Earthy, 0.7)]).Value;

        facets[Facet.Woody].ShouldBe(0.8);
        facets[Facet.Citrus].ShouldBe(0d);
        facets.Dominant.ShouldBe(Facet.Woody);
        Facets.None.Dominant.ShouldBeNull();
    }

    [Fact]
    public void Facets_with_the_same_weights_are_equal()
    {
        var a = Facets.Create([KeyValuePair.Create(Facet.Floral, 0.5)]).Value;
        var b = Facets.Create([KeyValuePair.Create(Facet.Floral, 0.5)]).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Volatility_rejects_non_positive_half_lives(double hours)
    {
        Volatility.FromHalfLifeHours(hours).Error.ShouldBe(VolatilityErrors.HalfLifeOutOfRange);
    }

    [Theory]
    [InlineData(0.5, NoteTier.Top)]
    [InlineData(1.99, NoteTier.Top)]
    [InlineData(2, NoteTier.Heart)]
    [InlineData(11.9, NoteTier.Heart)]
    [InlineData(12, NoteTier.Base)]
    [InlineData(80, NoteTier.Base)]
    public void Volatility_derives_the_note_tier(double hours, NoteTier expected)
    {
        Volatility.FromHalfLifeHours(hours).Value.Tier.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Material_rejects_a_non_positive_odor_strength(double strength)
    {
        CreateMaterial("Rose otto", strength).Error.ShouldBe(MaterialErrors.OdorStrengthOutOfRange);
    }

    [Fact]
    public void Material_requires_a_name()
    {
        CreateMaterial(" ", 1.0).Error.ShouldBe(MaterialErrors.NameRequired);
    }

    private static Perfumery.SharedKernel.Result<Material> CreateMaterial(string name, double strength) =>
        Material.Create(
            MaterialId.Create("rose-otto").Value,
            name,
            "Rosa damascena",
            "Kazanlak",
            ExtractionMethod.SteamDistillation,
            Facets.None,
            strength,
            Volatility.FromHalfLifeHours(6).Value,
            Money.Create(2400m).Value,
            Dilution.FromPercent(10m).Value);
}
