using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using static Perfumery.Domain.UnitTests.TestMaterials;

namespace Perfumery.Domain.UnitTests.Formulas;

public class FormulaTests
{
    [Fact]
    public void Create_starts_at_revision_1_and_raises_the_created_event()
    {
        var formula = Formula.Create("  Eau de Cologne  ").Value;

        formula.Name.ShouldBe("Eau de Cologne");
        formula.Revision.ShouldBe(1);
        formula.Lines.ShouldBeEmpty();
        var created = formula.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<FormulaCreatedDomainEvent>();
        created.FormulaId.ShouldBe(formula.Id);
        created.Revision.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_name(string? name)
    {
        Formula.Create(name).Error.ShouldBe(FormulaErrors.NameRequired);
    }

    [Fact]
    public void Create_limits_the_name_to_80_characters()
    {
        Formula.Create(new string('a', 80)).IsSuccess.ShouldBeTrue();
        Formula.Create(new string('a', 81)).Error.ShouldBe(FormulaErrors.NameTooLong);
    }

    [Fact]
    public void AddLine_defaults_to_the_material_stock_dilution()
    {
        var formula = NewFormula();

        formula.AddLine(RoseOtto, Parts(10m)).IsSuccess.ShouldBeTrue();
        formula.AddLine(Bergamot, Parts(10m)).IsSuccess.ShouldBeTrue();

        formula.Lines.Single(line => line.MaterialId == RoseOtto.Id).Dilution.ShouldBe(Percent(10m));
        formula.Lines.Single(line => line.MaterialId == Bergamot.Id).Dilution.ShouldBe(Dilution.Neat);
    }

    [Fact]
    public void AddLine_uses_an_explicit_dilution_when_given()
    {
        var formula = NewFormula();

        formula.AddLine(RoseOtto, Parts(10m), Percent(1m));

        formula.Lines.Single().Dilution.ShouldBe(Percent(1m));
    }

    [Fact]
    public void AddLine_rejects_a_material_already_in_the_formula()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));

        var result = formula.AddLine(Bergamot, Parts(5m));

        result.Error.Code.ShouldBe("Formula.MaterialAlreadyPresent");
        formula.Lines.Count.ShouldBe(1);
        formula.Revision.ShouldBe(2);
    }

    [Fact]
    public void AddLine_rejects_a_41st_line()
    {
        var formula = NewFormula();
        for (var i = 1; i <= Formula.MaxLines; i++)
        {
            formula.AddLine(Numbered(i), Parts(1m)).IsSuccess.ShouldBeTrue();
        }

        formula.AddLine(Numbered(41), Parts(1m)).Error.ShouldBe(FormulaErrors.TooManyLines);
        formula.Lines.Count.ShouldBe(40);
    }

    [Fact]
    public void Changing_an_unknown_line_fails_without_a_revision()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));

        formula.AdjustParts(Lemon.Id, Parts(1m)).Error.Code.ShouldBe("Formula.LineNotFound");
        formula.ChangeDilution(Lemon.Id, Percent(10m)).Error.Code.ShouldBe("Formula.LineNotFound");
        formula.RemoveLine(Lemon.Id).Error.Code.ShouldBe("Formula.LineNotFound");
        formula.NeatFraction(Lemon.Id).Error.Code.ShouldBe("Formula.LineNotFound");
        formula.Revision.ShouldBe(2);
    }

    [Fact]
    public void Neat_fractions_account_for_dilution()
    {
        // 10 parts bergamot neat + 10 parts rose otto at 10%, 20 parts in total.
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(RoseOtto, Parts(10m), Percent(10m));

        formula.TotalParts.ShouldBe(20m);
        formula.NeatFraction(Bergamot.Id).Value.ShouldBe(0.5m);
        formula.NeatFraction(RoseOtto.Id).Value.ShouldBe(0.05m);
        formula.SolventFraction.ShouldBe(0.45m);
    }

    [Fact]
    public void An_all_neat_formula_has_no_solvent_and_an_empty_one_has_none_either()
    {
        var formula = NewFormula();
        formula.SolventFraction.ShouldBe(0m);

        formula.AddLine(Bergamot, Parts(3m));
        formula.AddLine(Lemon, Parts(1m));

        formula.SolventFraction.ShouldBe(0m);
        formula.NeatFraction(Bergamot.Id).Value.ShouldBe(0.75m);
    }

    [Fact]
    public void Adjusting_parts_and_dilution_changes_the_fractions()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(RoseOtto, Parts(10m), Percent(10m));

        formula.AdjustParts(Bergamot.Id, Parts(30m)).IsSuccess.ShouldBeTrue();
        formula.ChangeDilution(RoseOtto.Id, Percent(20m)).IsSuccess.ShouldBeTrue();

        formula.NeatFraction(Bergamot.Id).Value.ShouldBe(0.75m);
        formula.NeatFraction(RoseOtto.Id).Value.ShouldBe(0.05m);
    }

    [Fact]
    public void RemoveLine_takes_the_material_out()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(Lemon, Parts(10m));

        formula.RemoveLine(Lemon.Id).IsSuccess.ShouldBeTrue();

        formula.Lines.ShouldHaveSingleItem().MaterialId.ShouldBe(Bergamot.Id);
        formula.NeatFraction(Bergamot.Id).Value.ShouldBe(1m);
    }

    [Fact]
    public void Every_successful_change_increments_the_revision_and_raises_the_revised_event()
    {
        var formula = NewFormula();
        formula.ClearDomainEvents();

        formula.AddLine(Bergamot, Parts(10m));
        formula.AdjustParts(Bergamot.Id, Parts(20m));
        formula.ChangeDilution(Bergamot.Id, Percent(50m));
        formula.RemoveLine(Bergamot.Id);

        formula.Revision.ShouldBe(5);
        formula.DomainEvents
            .ShouldAllBe(domainEvent => domainEvent is FormulaRevisedDomainEvent);
        formula.DomainEvents.Cast<FormulaRevisedDomainEvent>()
            .Select(domainEvent => (domainEvent.FormulaId, domainEvent.Revision))
            .ShouldBe([(formula.Id, 2), (formula.Id, 3), (formula.Id, 4), (formula.Id, 5)]);
    }

    [Fact]
    public void Setting_a_line_to_the_value_it_already_has_is_not_a_revision()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));
        formula.ClearDomainEvents();

        formula.AdjustParts(Bergamot.Id, Parts(10m)).IsSuccess.ShouldBeTrue();
        formula.ChangeDilution(Bergamot.Id, Dilution.Neat).IsSuccess.ShouldBeTrue();

        formula.Revision.ShouldBe(2);
        formula.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Cost_per_kg_weights_material_cost_by_neat_fraction()
    {
        // Bergamot 40/kg at a 0.5 neat fraction + rose otto 2400/kg at 0.05.
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(RoseOtto, Parts(10m), Percent(10m));

        var cost = formula.Compose().Value.CostPerKg(Lookup(Bergamot, RoseOtto), Money.Zero);

        cost.Value.Amount.ShouldBe(140m);
    }

    [Fact]
    public void Cost_fails_when_a_material_is_missing_from_the_lookup()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));

        formula.Compose().Value.CostPerKg(Lookup(Lemon), Money.Zero).Error.Code.ShouldBe("Material.NotFound");
    }

    private static Formula NewFormula() => Formula.Create("Test formula").Value;

    [Fact]
    public void Compose_freezes_the_neat_fractions_and_the_solvent_share()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(RoseOtto, Parts(10m), Percent(10m));

        var composition = formula.Compose().Value;
        formula.AdjustParts(Bergamot.Id, Parts(90m));

        composition.Lines.ShouldBe([new CompositionLine(Bergamot.Id, 0.5m), new CompositionLine(RoseOtto.Id, 0.05m)]);
        composition.SolventFraction.ShouldBe(0.45m);
        formula.Compose().Value.ShouldNotBe(composition);
    }

    [Fact]
    public void An_empty_formula_cannot_be_composed()
    {
        NewFormula().Compose().Error.ShouldBe(FormulaErrors.EmptyFormula);
    }

    [Fact]
    public void Composition_rejects_bad_fractions_and_duplicates()
    {
        Composition.Create([]).Error.ShouldBe(FormulaErrors.EmptyFormula);
        Composition.Create([new CompositionLine(Bergamot.Id, 0m)]).Error.ShouldBe(CompositionErrors.FractionOutOfRange);
        Composition.Create([new CompositionLine(Bergamot.Id, 0.7m), new CompositionLine(Lemon.Id, 0.7m)])
            .Error.ShouldBe(CompositionErrors.FractionsExceedWhole);
        Composition.Create([new CompositionLine(Bergamot.Id, 0.2m), new CompositionLine(Bergamot.Id, 0.2m)])
            .Error.ShouldBe(CompositionErrors.DuplicateMaterial);
    }

    [Fact]
    public void A_formula_survives_a_snapshot_round_trip()
    {
        var formula = NewFormula();
        formula.AddLine(Bergamot, Parts(10m));
        formula.AddLine(RoseOtto, Parts(2.5m), Percent(10m));

        var restored = Formula.Restore(formula.ToSnapshot()).Value;

        restored.Id.ShouldBe(formula.Id);
        restored.Name.ShouldBe(formula.Name);
        restored.Revision.ShouldBe(3);
        restored.Compose().Value.ShouldBe(formula.Compose().Value);
        restored.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Restore_re_checks_the_invariants()
    {
        var snapshot = NewFormula().ToSnapshot();
        var line = new FormulaLineSnapshot(Bergamot.Id.Value, 10m, 1m);

        Formula.Restore(snapshot with { Name = " " }).Error.ShouldBe(FormulaErrors.NameRequired);
        Formula.Restore(snapshot with { Revision = 0 }).Error.ShouldBe(FormulaErrors.InvalidRevision);
        Formula.Restore(snapshot with { Lines = [line, line] }).Error.Code.ShouldBe("Formula.MaterialAlreadyPresent");
        Formula.Restore(snapshot with { Lines = [line with { Parts = 0m }] }).Error.ShouldBe(PartsErrors.OutOfRange);
        Formula.Restore(snapshot with { Lines = [line with { DilutionFraction = 2m }] }).Error.ShouldBe(DilutionErrors.OutOfRange);
        Formula.Restore(snapshot with { Lines = [line with { MaterialId = "Not A Slug" }] }).Error.ShouldBe(MaterialErrors.IdMalformed);
    }
}
