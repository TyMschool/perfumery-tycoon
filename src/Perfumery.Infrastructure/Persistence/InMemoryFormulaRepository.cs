using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;

namespace Perfumery.Infrastructure.Persistence;

/// <summary>
/// A view over the formulas of the running game. Writing them to disk is the save
/// decorator's job, so saving here only checks the formula is known.
/// </summary>
internal sealed class InMemoryFormulaRepository(IGameSession session) : IFormulaRepository
{
    public Task<Formula?> GetByIdAsync(FormulaId id, CancellationToken cancellationToken) =>
        Task.FromResult(session.Find(id));

    public Task AddAsync(Formula formula, CancellationToken cancellationToken)
    {
        if (session.Find(formula.Id) is not null)
        {
            throw new InvalidOperationException($"Formula '{formula.Id}' has already been added.");
        }

        session.Add(formula);
        formula.ClearDomainEvents();
        return Task.CompletedTask;
    }

    public Task SaveAsync(Formula formula, CancellationToken cancellationToken)
    {
        if (session.Find(formula.Id) is null)
        {
            throw new InvalidOperationException($"Formula '{formula.Id}' was never added.");
        }

        formula.ClearDomainEvents();
        return Task.CompletedTask;
    }
}
