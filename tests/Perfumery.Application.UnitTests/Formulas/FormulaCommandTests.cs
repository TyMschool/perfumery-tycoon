using Perfumery.Application.Formulas.AddFormulaLine;
using Perfumery.Application.Formulas.AdjustLineParts;
using Perfumery.Application.Formulas.ChangeLineDilution;
using Perfumery.Application.Formulas.CreateFormula;
using Perfumery.Application.Formulas.RemoveFormulaLine;
using Perfumery.Application.UnitTests.Fakes;
using Perfumery.Domain.Formulas;
using Perfumery.SharedKernel;
using static Perfumery.Application.UnitTests.Fakes.FakeMaterialCatalog;

namespace Perfumery.Application.UnitTests.Formulas;

public class FormulaCommandTests
{
    private readonly FakeFormulaRepository _formulas = new();
    private readonly FakeMaterialCatalog _catalog = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateFormula_stores_the_formula_and_returns_its_id()
    {
        var result = await new CreateFormulaCommandHandler(_formulas).Handle(new CreateFormulaCommand("Eau de Cologne"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = _formulas.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(result.Value);
        stored.Name.ShouldBe("Eau de Cologne");
    }

    [Fact]
    public async Task CreateFormula_returns_the_domain_error_and_stores_nothing()
    {
        var result = await new CreateFormulaCommandHandler(_formulas).Handle(new CreateFormulaCommand("  "), Ct);

        result.Error.ShouldBe(FormulaErrors.NameRequired);
        _formulas.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddFormulaLine_uses_the_stock_dilution_by_default_and_saves()
    {
        var formula = _formulas.Seed();

        var result = await Add(new AddFormulaLineCommand(formula.Id.Value, RoseOttoId, 10m));

        result.IsSuccess.ShouldBeTrue();
        var line = formula.Lines.ShouldHaveSingleItem();
        line.MaterialId.Value.ShouldBe(RoseOttoId);
        line.Parts.Value.ShouldBe(10m);
        line.Dilution.Percent.ShouldBe(10m);
        _formulas.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task AddFormulaLine_takes_an_explicit_dilution()
    {
        var formula = _formulas.Seed();

        await Add(new AddFormulaLineCommand(formula.Id.Value, RoseOttoId, 10m, DilutionPercent: 1m));

        formula.Lines.Single().Dilution.Percent.ShouldBe(1m);
    }

    [Fact]
    public async Task AddFormulaLine_fails_for_an_unknown_formula()
    {
        var result = await Add(new AddFormulaLineCommand(Guid.NewGuid(), BergamotId, 10m));

        result.Error.Code.ShouldBe("Formula.NotFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Theory]
    [InlineData("oakmoss-yugoslavia", "Material.NotFound")]
    [InlineData("Not A Slug", "Material.IdMalformed")]
    public async Task AddFormulaLine_fails_for_an_unknown_or_malformed_material(string materialId, string expectedCode)
    {
        var formula = _formulas.Seed();

        var result = await Add(new AddFormulaLineCommand(formula.Id.Value, materialId, 10m));

        result.Error.Code.ShouldBe(expectedCode);
        formula.Lines.ShouldBeEmpty();
        _formulas.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task AddFormulaLine_returns_value_object_range_errors()
    {
        var formula = _formulas.Seed();

        (await Add(new AddFormulaLineCommand(formula.Id.Value, BergamotId, 0m))).Error.ShouldBe(PartsErrors.OutOfRange);
        (await Add(new AddFormulaLineCommand(formula.Id.Value, BergamotId, 10m, 150m))).Error.ShouldBe(DilutionErrors.OutOfRange);
        _formulas.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task AddFormulaLine_returns_the_aggregate_error_for_a_duplicate()
    {
        var formula = _formulas.Seed();
        await Add(new AddFormulaLineCommand(formula.Id.Value, BergamotId, 10m));

        var result = await Add(new AddFormulaLineCommand(formula.Id.Value, BergamotId, 5m));

        result.Error.Code.ShouldBe("Formula.MaterialAlreadyPresent");
        _formulas.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task AdjustLineParts_changes_the_line_and_saves()
    {
        var formula = await SeedWithBergamot();
        var handler = new AdjustLinePartsCommandHandler(_formulas);

        var result = await handler.Handle(new AdjustLinePartsCommand(formula.Id.Value, BergamotId, 25m), Ct);

        result.IsSuccess.ShouldBeTrue();
        formula.Lines.Single().Parts.Value.ShouldBe(25m);
        _formulas.SaveCount.ShouldBe(2);
    }

    [Fact]
    public async Task AdjustLineParts_fails_for_bad_input_without_saving()
    {
        var formula = await SeedWithBergamot();
        var handler = new AdjustLinePartsCommandHandler(_formulas);

        (await handler.Handle(new AdjustLinePartsCommand(Guid.NewGuid(), BergamotId, 25m), Ct)).Error.Code.ShouldBe("Formula.NotFound");
        (await handler.Handle(new AdjustLinePartsCommand(formula.Id.Value, RoseOttoId, 25m), Ct)).Error.Code.ShouldBe("Formula.LineNotFound");
        (await handler.Handle(new AdjustLinePartsCommand(formula.Id.Value, BergamotId, 5000m), Ct)).Error.ShouldBe(PartsErrors.OutOfRange);
        formula.Lines.Single().Parts.Value.ShouldBe(10m);
        _formulas.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ChangeLineDilution_changes_the_line_and_saves()
    {
        var formula = await SeedWithBergamot();
        var handler = new ChangeLineDilutionCommandHandler(_formulas);

        var result = await handler.Handle(new ChangeLineDilutionCommand(formula.Id.Value, BergamotId, 50m), Ct);

        result.IsSuccess.ShouldBeTrue();
        formula.Lines.Single().Dilution.Percent.ShouldBe(50m);
        _formulas.SaveCount.ShouldBe(2);
    }

    [Fact]
    public async Task ChangeLineDilution_fails_for_bad_input_without_saving()
    {
        var formula = await SeedWithBergamot();
        var handler = new ChangeLineDilutionCommandHandler(_formulas);

        (await handler.Handle(new ChangeLineDilutionCommand(Guid.NewGuid(), BergamotId, 50m), Ct)).Error.Code.ShouldBe("Formula.NotFound");
        (await handler.Handle(new ChangeLineDilutionCommand(formula.Id.Value, RoseOttoId, 50m), Ct)).Error.Code.ShouldBe("Formula.LineNotFound");
        (await handler.Handle(new ChangeLineDilutionCommand(formula.Id.Value, BergamotId, 0m), Ct)).Error.ShouldBe(DilutionErrors.OutOfRange);
        _formulas.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveFormulaLine_removes_the_line_and_saves()
    {
        var formula = await SeedWithBergamot();
        var handler = new RemoveFormulaLineCommandHandler(_formulas);

        var result = await handler.Handle(new RemoveFormulaLineCommand(formula.Id.Value, BergamotId), Ct);

        result.IsSuccess.ShouldBeTrue();
        formula.Lines.ShouldBeEmpty();
        _formulas.SaveCount.ShouldBe(2);
    }

    [Fact]
    public async Task RemoveFormulaLine_fails_for_an_unknown_formula_or_line()
    {
        var formula = await SeedWithBergamot();
        var handler = new RemoveFormulaLineCommandHandler(_formulas);

        (await handler.Handle(new RemoveFormulaLineCommand(Guid.NewGuid(), BergamotId), Ct)).Error.Code.ShouldBe("Formula.NotFound");
        (await handler.Handle(new RemoveFormulaLineCommand(formula.Id.Value, RoseOttoId), Ct)).Error.Code.ShouldBe("Formula.LineNotFound");
        formula.Lines.Count.ShouldBe(1);
    }

    private Task<Result> Add(AddFormulaLineCommand command) =>
        new AddFormulaLineCommandHandler(_formulas, _catalog).Handle(command, Ct);

    private async Task<Formula> SeedWithBergamot()
    {
        var formula = _formulas.Seed();
        (await Add(new AddFormulaLineCommand(formula.Id.Value, BergamotId, 10m))).IsSuccess.ShouldBeTrue();
        return formula;
    }
}
