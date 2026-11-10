using FluentValidation;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Formulas.CreateFormula;

public sealed record CreateFormulaCommand(string Name) : ICommand<Result<FormulaId>>;

internal sealed class CreateFormulaCommandValidator : AbstractValidator<CreateFormulaCommand>
{
    public CreateFormulaCommandValidator()
    {
        RuleFor(command => command.Name).NotNull();
    }
}

internal sealed class CreateFormulaCommandHandler(IFormulaRepository formulas)
    : ICommandHandler<CreateFormulaCommand, Result<FormulaId>>
{
    public async Task<Result<FormulaId>> Handle(CreateFormulaCommand command, CancellationToken cancellationToken)
    {
        var formula = Formula.Create(command.Name);
        if (formula.IsFailure)
        {
            return formula.Error;
        }

        await formulas.AddAsync(formula.Value, cancellationToken);
        return formula.Value.Id;
    }
}
