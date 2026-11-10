using FluentValidation;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Game;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Production.CompoundConcentrate;

/// <param name="FormulaId">The formula to weigh out, at its current revision.</param>
/// <param name="Grams">Mass of concentrate to make.</param>
public sealed record CompoundConcentrateCommand(Guid FormulaId, decimal Grams) : ICommand<Result<ConcentrateBatchId>>;

internal sealed class CompoundConcentrateCommandValidator : AbstractValidator<CompoundConcentrateCommand>
{
    public CompoundConcentrateCommandValidator()
    {
        RuleFor(command => command.FormulaId).NotEmpty();
    }
}

internal sealed class CompoundConcentrateCommandHandler(
    IGameSession session,
    IGameClock clock,
    StockItemDirectory items,
    AtelierSettings settings)
    : ICommandHandler<CompoundConcentrateCommand, Result<ConcentrateBatchId>>
{
    public Task<Result<ConcentrateBatchId>> Handle(CompoundConcentrateCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(command));

    private Result<ConcentrateBatchId> Handle(CompoundConcentrateCommand command)
    {
        if (!session.IsStarted)
        {
            return GameErrors.NotStarted;
        }

        var formulaId = new FormulaId(command.FormulaId);
        var formula = session.Find(formulaId);
        if (formula is null)
        {
            return FormulaErrors.NotFound(formulaId);
        }

        var composition = formula.Compose();
        var mass = Mass.FromGrams(command.Grams);
        if (Result.FirstFailure(composition, mass) is { } failure)
        {
            return failure;
        }

        var now = session.Atelier.Observe(clock.UtcNow);
        var batch = CompoundingService.Compound(
            session.Inventory, formula.CurrentRevision, composition.Value, mass.Value, settings, items.Spirit, now);
        if (batch.IsFailure)
        {
            return batch.Error;
        }

        session.Add(batch.Value);
        return batch.Value.Id;
    }
}
