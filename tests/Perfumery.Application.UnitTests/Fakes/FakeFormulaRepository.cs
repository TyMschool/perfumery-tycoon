using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;

namespace Perfumery.Application.UnitTests.Fakes;

internal sealed class FakeFormulaRepository : IFormulaRepository
{
    private readonly Dictionary<FormulaId, Formula> _formulas = [];

    public int SaveCount { get; private set; }

    public IReadOnlyCollection<Formula> All => _formulas.Values;

    public Formula Seed(string name = "Test formula")
    {
        var formula = Formula.Create(name).Value;
        _formulas.Add(formula.Id, formula);
        return formula;
    }

    public Task<Formula?> GetByIdAsync(FormulaId id, CancellationToken cancellationToken) =>
        Task.FromResult(_formulas.GetValueOrDefault(id));

    public Task AddAsync(Formula formula, CancellationToken cancellationToken)
    {
        _formulas.Add(formula.Id, formula);
        return Task.CompletedTask;
    }

    public Task SaveAsync(Formula formula, CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
