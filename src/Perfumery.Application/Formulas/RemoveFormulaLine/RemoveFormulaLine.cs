using FluentValidation;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Formulas.RemoveFormulaLine;

public sealed record RemoveFormulaLineCommand(Guid FormulaId, string MaterialId) : ICommand;

internal sealed class RemoveFormulaLineCommandValidator : AbstractValidator<RemoveFormulaLineCommand>
{
    public RemoveFormulaLineCommandValidator()
    {
        RuleFor(command => command.FormulaId).NotEmpty();
        RuleFor(command => command.MaterialId).NotEmpty();
    }
}

internal sealed class RemoveFormulaLineCommandHandler(IFormulaRepository formulas)
    : ICommandHandler<RemoveFormulaLineCommand, Result>
{
    public Task<Result> Handle(RemoveFormulaLineCommand command, CancellationToken cancellationToken) =>
        FormulaLineEditor.Edit(
            formulas,
            command.FormulaId,
            command.MaterialId,
            (formula, materialId) => formula.RemoveLine(materialId),
            cancellationToken);
}
