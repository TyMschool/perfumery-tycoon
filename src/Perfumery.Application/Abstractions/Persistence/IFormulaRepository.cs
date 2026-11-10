using Perfumery.Domain.Formulas;

namespace Perfumery.Application.Abstractions.Persistence;

public interface IFormulaRepository
{
    Task<Formula?> GetByIdAsync(FormulaId id, CancellationToken cancellationToken);

    Task AddAsync(Formula formula, CancellationToken cancellationToken);

    Task SaveAsync(Formula formula, CancellationToken cancellationToken);
}
