using System.Collections.Concurrent;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;

namespace Perfumery.Infrastructure.Persistence;

/// <summary>
/// Phase 1 persistence: formulas live in memory for the lifetime of the process.
/// Domain events are cleared on save, where a real store would dispatch them.
/// </summary>
internal sealed class InMemoryFormulaRepository : IFormulaRepository
{
    private readonly ConcurrentDictionary<FormulaId, Formula> _formulas = new();

    public Task<Formula?> GetByIdAsync(FormulaId id, CancellationToken cancellationToken) =>
        Task.FromResult(_formulas.GetValueOrDefault(id));

    public Task AddAsync(Formula formula, CancellationToken cancellationToken)
    {
        if (!_formulas.TryAdd(formula.Id, formula))
        {
            throw new InvalidOperationException($"Formula '{formula.Id}' has already been added.");
        }

        formula.ClearDomainEvents();
        return Task.CompletedTask;
    }

    public Task SaveAsync(Formula formula, CancellationToken cancellationToken)
    {
        if (!_formulas.ContainsKey(formula.Id))
        {
            throw new InvalidOperationException($"Formula '{formula.Id}' was never added.");
        }

        _formulas[formula.Id] = formula;
        formula.ClearDomainEvents();
        return Task.CompletedTask;
    }
}
