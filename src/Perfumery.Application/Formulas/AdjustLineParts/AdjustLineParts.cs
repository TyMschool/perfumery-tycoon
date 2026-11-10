using FluentValidation;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Formulas.AdjustLineParts;

public sealed record AdjustLinePartsCommand(Guid FormulaId, string MaterialId, decimal Parts) : ICommand;

internal sealed class AdjustLinePartsCommandValidator : AbstractValidator<AdjustLinePartsCommand>
{
    public AdjustLinePartsCommandValidator()
    {
        RuleFor(command => command.FormulaId).NotEmpty();
        RuleFor(command => command.MaterialId).NotEmpty();
    }
}

internal sealed class AdjustLinePartsCommandHandler(IFormulaRepository formulas)
    : ICommandHandler<AdjustLinePartsCommand, Result>
{
    public Task<Result> Handle(AdjustLinePartsCommand command, CancellationToken cancellationToken) =>
        FormulaLineEditor.Edit(
            formulas,
            command.FormulaId,
            command.MaterialId,
            (formula, materialId) =>
            {
                var parts = Parts.Create(command.Parts);
                return parts.IsFailure ? parts.Error : formula.AdjustParts(materialId, parts.Value);
            },
            cancellationToken);
}
