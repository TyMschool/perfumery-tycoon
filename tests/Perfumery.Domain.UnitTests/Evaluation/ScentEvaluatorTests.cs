using Perfumery.Domain.Evaluation;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using static Perfumery.Domain.UnitTests.TestMaterials;

namespace Perfumery.Domain.UnitTests.Evaluation;

public class ScentEvaluatorTests
{
    private static readonly IReadOnlyDictionary<MaterialId, Material> Materials =
        Lookup(Bergamot, Lemon, Neroli, Petitgrain, Rosemary, RoseOtto, Vetiver, Sandalwood);

    private readonly ScentEvaluator _evaluator = new(EvaluationSettings.Default);

    [Fact]
    public void An_empty_formula_cannot_be_evaluated()
    {
        var formula = Formula.Create("Empty").Value;

        _evaluator.Evaluate(formula, Materials).Error.ShouldBe(FormulaErrors.EmptyFormula);
    }

    [Fact]
    public void A_formula_with_a_material_missing_from_the_lookup_fails()
    {
        var formula = Formula.Create("Orphan").Value;
        formula.AddLine(Bergamot, Parts(10m));

        _evaluator.Evaluate(formula, Lookup(Lemon)).Error.Code.ShouldBe("Material.NotFound");
    }

    [Fact]
    public void The_profile_has_the_five_checkpoints_in_order()
    {
        var profile = _evaluator.Evaluate(EauDeCologne(), Materials).Value;

        profile.Checkpoints.Select(checkpoint => checkpoint.Elapsed).ShouldBe(
        [
            TimeSpan.Zero,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(24),
        ]);
    }

    [Fact]
    public void Facet_shapes_sum_to_1_and_at_most_three_contributors_are_listed()
    {
        var profile = _evaluator.Evaluate(EauDeCologne(), Materials).Value;

        foreach (var checkpoint in profile.Checkpoints)
        {
            checkpoint.Facets.Sum.ShouldBe(1d, 1e-9);
            checkpoint.TopContributors.Count.ShouldBe(3);
            checkpoint.TopContributors
                .Select(contributor => contributor.PerceivedIntensity)
                .ShouldBeInOrder(SortDirection.Descending);
        }
    }

    [Fact]
    public void An_eau_de_cologne_opens_citrus_and_has_mostly_faded_by_four_hours()
    {
        var profile = _evaluator.Evaluate(EauDeCologne(), Materials).Value;

        At(profile, TimeSpan.Zero).DominantFacet.ShouldBe(Facet.Citrus);
        At(profile, TimeSpan.FromMinutes(15)).DominantFacet.ShouldBe(Facet.Citrus);

        var start = At(profile, TimeSpan.Zero).TotalIntensity;
        At(profile, TimeSpan.FromHours(4)).TotalIntensity.ShouldBeLessThan(start * 0.5);
    }

    [Fact]
    public void Intensity_never_rises_over_time()
    {
        var profile = _evaluator.Evaluate(EauDeCologne(), Materials).Value;

        profile.Checkpoints
            .Select(checkpoint => checkpoint.TotalIntensity)
            .ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public void Sandalwood_and_vetiver_make_the_drydown_woody()
    {
        var plain = _evaluator.Evaluate(EauDeCologne(), Materials).Value;

        var based = EauDeCologne();
        based.AddLine(Sandalwood, Parts(10m));
        based.AddLine(Vetiver, Parts(5m));
        var profile = _evaluator.Evaluate(based, Materials).Value;

        At(plain, TimeSpan.FromHours(24)).DominantFacet.ShouldNotBe(Facet.Woody);
        At(profile, TimeSpan.FromHours(24)).DominantFacet.ShouldBe(Facet.Woody);
        At(profile, TimeSpan.Zero).DominantFacet.ShouldBe(Facet.Citrus);
    }

    [Fact]
    public void Doubling_a_material_raises_its_share_less_than_twofold()
    {
        double ShareOfRosemary(decimal parts)
        {
            var formula = Formula.Create("Trio").Value;
            formula.AddLine(Bergamot, Parts(10m));
            formula.AddLine(Lemon, Parts(10m));
            formula.AddLine(Rosemary, Parts(parts));

            return At(_evaluator.Evaluate(formula, Materials).Value, TimeSpan.Zero)
                .TopContributors.Single(contributor => contributor.MaterialId == Rosemary.Id)
                .Share;
        }

        var single = ShareOfRosemary(2m);
        var doubled = ShareOfRosemary(4m);

        doubled.ShouldBeGreaterThan(single);
        doubled.ShouldBeLessThan(single * 2d);
    }

    [Fact]
    public void A_stronger_dilution_of_the_same_parts_smells_weaker()
    {
        double RoseIntensity(decimal percent)
        {
            var formula = Formula.Create("Rose").Value;
            formula.AddLine(RoseOtto, Parts(10m), Percent(percent));
            formula.AddLine(Bergamot, Parts(10m));

            return At(_evaluator.Evaluate(formula, Materials).Value, TimeSpan.Zero)
                .TopContributors.Single(contributor => contributor.MaterialId == RoseOtto.Id)
                .PerceivedIntensity;
        }

        RoseIntensity(1m).ShouldBeLessThan(RoseIntensity(10m));
    }

    [Fact]
    public void The_same_input_always_gives_the_same_profile()
    {
        var formula = EauDeCologne();

        var first = _evaluator.Evaluate(formula, Materials).Value;
        var second = _evaluator.Evaluate(formula, Materials).Value;

        second.CostPerKgOfConcentrate.ShouldBe(first.CostPerKgOfConcentrate);
        second.Checkpoints.Count.ShouldBe(first.Checkpoints.Count);
        for (var i = 0; i < first.Checkpoints.Count; i++)
        {
            second.Checkpoints[i].Elapsed.ShouldBe(first.Checkpoints[i].Elapsed);
            second.Checkpoints[i].TotalIntensity.ShouldBe(first.Checkpoints[i].TotalIntensity);
            second.Checkpoints[i].Facets.ShouldBe(first.Checkpoints[i].Facets);
            second.Checkpoints[i].TopContributors.ShouldBe(first.Checkpoints[i].TopContributors);
        }
    }

    [Fact]
    public void The_profile_reports_the_cost_of_the_concentrate()
    {
        var formula = Formula.Create("Rose and bergamot").Value;
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(RoseOtto, Parts(10m), Percent(10m));

        _evaluator.Evaluate(formula, Materials).Value.CostPerKgOfConcentrate.Amount.ShouldBe(140m);
    }

    [Fact]
    public void A_single_neat_material_matches_the_formulas_by_hand()
    {
        // Rosemary alone: n = 1, s = 1.5, h = 1 h, k = 0.5.
        var formula = Formula.Create("Rosemary water").Value;
        formula.AddLine(Rosemary, Parts(1m));

        var profile = _evaluator.Evaluate(formula, Materials).Value;

        At(profile, TimeSpan.Zero).TotalIntensity.ShouldBe(Math.Sqrt(1.5), 1e-12);
        At(profile, TimeSpan.FromHours(1)).TotalIntensity.ShouldBe(Math.Sqrt(1.5 * 0.5), 1e-12);
        At(profile, TimeSpan.FromHours(4)).TotalIntensity.ShouldBe(Math.Sqrt(1.5 / 16d), 1e-12);
        // Facets 0.9 aromatic, 0.4 green, 0.15 woody, normalized over 1.45.
        At(profile, TimeSpan.Zero).Facets[Facet.Aromatic].ShouldBe(0.9 / 1.45, 1e-12);
        At(profile, TimeSpan.Zero).TopContributors.ShouldHaveSingleItem().Share.ShouldBe(1d, 1e-12);
    }

    [Fact]
    public void Evaporation_halves_the_remaining_share_every_half_life()
    {
        var volatility = Volatility.FromHalfLifeHours(2).Value;

        EvaporationModel.RemainingShare(volatility, TimeSpan.Zero).ShouldBe(1d);
        EvaporationModel.RemainingShare(volatility, TimeSpan.FromHours(2)).ShouldBe(0.5, 1e-12);
        EvaporationModel.RemainingShare(volatility, TimeSpan.FromHours(6)).ShouldBe(0.125, 1e-12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Settings_reject_an_exponent_outside_0_to_1(double exponent)
    {
        EvaluationSettings.Create(exponent, [TimeSpan.Zero]).Error.ShouldBe(EvaluationErrors.ExponentOutOfRange);
    }

    [Fact]
    public void Settings_reject_missing_unordered_or_negative_checkpoints()
    {
        EvaluationSettings.Create(0.5, []).Error.ShouldBe(EvaluationErrors.InvalidCheckpoints);
        EvaluationSettings.Create(0.5, [TimeSpan.FromHours(1), TimeSpan.Zero]).Error.ShouldBe(EvaluationErrors.InvalidCheckpoints);
        EvaluationSettings.Create(0.5, [TimeSpan.FromHours(-1)]).Error.ShouldBe(EvaluationErrors.InvalidCheckpoints);
    }

    [Fact]
    public void The_exponent_is_a_tuning_parameter()
    {
        // With k = 1 perception is linear, so doubling parts in a two-material blend
        // changes shares exactly as it changes neat fractions.
        var linear = new ScentEvaluator(EvaluationSettings.Create(1.0, [TimeSpan.Zero]).Value);
        var formula = Formula.Create("Linear").Value;
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(Sandalwood, Parts(10m));

        var checkpoint = linear.Evaluate(formula, Materials).Value.Checkpoints.Single();

        // Bergamot strength 1.0 against sandalwood 0.8 at equal neat fractions.
        checkpoint.TopContributors[0].MaterialId.ShouldBe(Bergamot.Id);
        checkpoint.TopContributors[0].Share.ShouldBe(1.0 / 1.8, 1e-12);
    }

    private static Formula EauDeCologne()
    {
        var formula = Formula.Create("Eau de Cologne").Value;
        formula.AddLine(Bergamot, Parts(40m));
        formula.AddLine(Lemon, Parts(20m));
        formula.AddLine(Neroli, Parts(10m));
        formula.AddLine(Petitgrain, Parts(10m));
        formula.AddLine(Rosemary, Parts(5m));
        return formula;
    }

    private static ScentCheckpoint At(ScentProfile profile, TimeSpan elapsed) =>
        profile.Checkpoints.Single(checkpoint => checkpoint.Elapsed == elapsed);
}
