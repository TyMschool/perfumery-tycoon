using Perfumery.Application.Formulas.EvaluateFormula;
using Perfumery.Application.UnitTests.Fakes;
using Perfumery.Domain.Evaluation;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using static Perfumery.Application.UnitTests.Fakes.FakeMaterialCatalog;

namespace Perfumery.Application.UnitTests.Formulas;

public class EvaluateFormulaTests
{
    private readonly FakeFormulaRepository _formulas = new();
    private readonly FakeMaterialCatalog _catalog = new();
    private readonly EvaluateFormulaQueryHandler _handler;

    public EvaluateFormulaTests()
    {
        _handler = new EvaluateFormulaQueryHandler(_formulas, _catalog, new ScentEvaluator(EvaluationSettings.Default));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_a_dto_profile_of_the_formula()
    {
        var formula = SeedRoseAndBergamot();

        var result = await _handler.Handle(new EvaluateFormulaQuery(formula.Id.Value), Ct);

        var profile = result.Value;
        profile.FormulaId.ShouldBe(formula.Id.Value);
        profile.FormulaName.ShouldBe("Rose water");
        profile.Revision.ShouldBe(3);
        profile.CostPerKgOfConcentrate.ShouldBe(140m);
        profile.SolventFraction.ShouldBe(0.45m);
        profile.Checkpoints.Select(checkpoint => checkpoint.ElapsedMinutes).ShouldBe([0d, 15d, 60d, 240d, 1440d]);

        var opening = profile.Checkpoints[0];
        opening.Facets.Keys.ShouldBe(Enum.GetNames<Facet>(), ignoreOrder: true);
        opening.Facets.Values.Sum().ShouldBe(1d, 1e-9);
        opening.DominantFacet.ShouldNotBeNull();
        opening.TopContributors.Select(contributor => contributor.MaterialName)
            .ShouldBe(["Bergamot oil", "Rose otto"], ignoreOrder: true);
        opening.TopContributors.Sum(contributor => contributor.Share).ShouldBe(1d, 1e-9);

        // Bergamot is gone a day later; the rose is what remains.
        profile.Checkpoints[^1].DominantFacet.ShouldBe("Floral");
    }

    [Fact]
    public async Task Fails_for_an_unknown_formula()
    {
        var result = await _handler.Handle(new EvaluateFormulaQuery(Guid.NewGuid()), Ct);

        result.Error.Code.ShouldBe("Formula.NotFound");
    }

    [Fact]
    public async Task Fails_for_an_empty_formula()
    {
        var formula = _formulas.Seed();

        var result = await _handler.Handle(new EvaluateFormulaQuery(formula.Id.Value), Ct);

        result.Error.ShouldBe(FormulaErrors.EmptyFormula);
    }

    [Fact]
    public async Task Fails_when_a_line_points_at_a_material_no_longer_in_the_catalog()
    {
        var formula = SeedRoseAndBergamot();
        _catalog.Remove(RoseOttoId);

        var result = await _handler.Handle(new EvaluateFormulaQuery(formula.Id.Value), Ct);

        result.Error.Code.ShouldBe("Material.NotFound");
    }

    private Formula SeedRoseAndBergamot()
    {
        var formula = _formulas.Seed("Rose water");
        formula.AddLine(_catalog.Find(MaterialId.Create(BergamotId).Value)!, Parts.Create(10m).Value);
        formula.AddLine(_catalog.Find(MaterialId.Create(RoseOttoId).Value)!, Parts.Create(10m).Value);
        return formula;
    }
}
