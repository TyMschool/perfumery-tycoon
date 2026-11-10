using Perfumery.Domain.Formulas;
using Perfumery.Infrastructure.Game;
using Perfumery.Infrastructure.Persistence;

namespace Perfumery.Infrastructure.UnitTests;

public class InMemoryFormulaRepositoryTests
{
    private readonly InMemoryFormulaRepository _repository = new(new InMemoryGameSession());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_added_formula_can_be_loaded_by_id()
    {
        var formula = Formula.Create("Lavender water").Value;

        await _repository.AddAsync(formula, Ct);

        (await _repository.GetByIdAsync(formula.Id, Ct)).ShouldBeSameAs(formula);
    }

    [Fact]
    public async Task An_unknown_id_loads_nothing()
    {
        (await _repository.GetByIdAsync(FormulaId.New(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Adding_twice_or_saving_an_unknown_formula_is_a_bug()
    {
        var formula = Formula.Create("Lavender water").Value;
        await _repository.AddAsync(formula, Ct);

        await Should.ThrowAsync<InvalidOperationException>(() => _repository.AddAsync(formula, Ct));
        await Should.ThrowAsync<InvalidOperationException>(
            () => _repository.SaveAsync(Formula.Create("Stranger").Value, Ct));
    }

    [Fact]
    public async Task Saving_clears_the_collected_domain_events()
    {
        var formula = Formula.Create("Lavender water").Value;
        formula.DomainEvents.ShouldNotBeEmpty();

        await _repository.AddAsync(formula, Ct);

        formula.DomainEvents.ShouldBeEmpty();
    }
}
