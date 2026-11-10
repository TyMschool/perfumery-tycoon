using FluentValidation;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Formulas.ChangeLineDilution;

public sealed record ChangeLineDilutionCommand(Guid FormulaId, string MaterialId, decimal DilutionPercent) : ICommand;

internal sealed class ChangeLineDilutionCommandValidator : AbstractValidator<ChangeLineDilutionCommand>
{
    public ChangeLineDilutionCommandValidator()
    {
        RuleFor(command => command.FormulaId).NotEmpty();
        RuleFor(command => command.MaterialId).NotEmpty();
    }
}

internal sealed class ChangeLineDilutionCommandHandler(IFormulaRepository formulas)
    : ICommandHandler<ChangeLineDilutionCommand, Result>
{
    public Task<Result> Handle(ChangeLineDilutionCommand command, CancellationToken cancellationToken) =>
        FormulaLineEditor.Edit(
            formulas,
            command.FormulaId,
            command.MaterialId,
            (formula, materialId) =>
            {
                var dilution = Dilution.FromPercent(command.DilutionPercent);
                return dilution.IsFailure ? dilution.Error : formula.ChangeDilution(materialId, dilution.Value);
            },
            cancellationToken);
}
