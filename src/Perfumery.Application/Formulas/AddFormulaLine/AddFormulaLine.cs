using FluentValidation;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Formulas.AddFormulaLine;

/// <param name="FormulaId">The formula to add to.</param>
/// <param name="MaterialId">Catalog slug of the material.</param>
/// <param name="Parts">Amount in parts by weight.</param>
/// <param name="DilutionPercent">Stock dilution in percent; null uses the material's lab stock dilution.</param>
public sealed record AddFormulaLineCommand(Guid FormulaId, string MaterialId, decimal Parts, decimal? DilutionPercent = null)
    : ICommand;

internal sealed class AddFormulaLineCommandValidator : AbstractValidator<AddFormulaLineCommand>
{
    public AddFormulaLineCommandValidator()
    {
        RuleFor(command => command.FormulaId).NotEmpty();
        RuleFor(command => command.MaterialId).NotEmpty();
    }
}

internal sealed class AddFormulaLineCommandHandler(IFormulaRepository formulas, IMaterialCatalog catalog)
    : ICommandHandler<AddFormulaLineCommand, Result>
{
    public Task<Result> Handle(AddFormulaLineCommand command, CancellationToken cancellationToken) =>
        FormulaLineEditor.Edit(
            formulas,
            command.FormulaId,
            command.MaterialId,
            (formula, materialId) =>
            {
                var material = catalog.Find(materialId);
                if (material is null)
                {
                    return MaterialErrors.NotFound(materialId);
                }

                var parts = Parts.Create(command.Parts);
                if (parts.IsFailure)
                {
                    return parts.Error;
                }

                Dilution? dilution = null;
                if (command.DilutionPercent is { } percent)
                {
                    var parsed = Dilution.FromPercent(percent);
                    if (parsed.IsFailure)
                    {
                        return parsed.Error;
                    }

                    dilution = parsed.Value;
                }

                return formula.AddLine(material, parts.Value, dilution);
            },
            cancellationToken);
}
