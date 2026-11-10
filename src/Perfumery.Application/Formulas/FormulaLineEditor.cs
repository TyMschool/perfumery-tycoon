using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Formulas;

/// <summary>The load → change → save steps shared by the commands that edit one line of a formula.</summary>
internal static class FormulaLineEditor
{
    public static async Task<Result> Edit(
        IFormulaRepository formulas,
        Guid formulaId,
        string materialId,
        Func<Formula, MaterialId, Result> change,
        CancellationToken cancellationToken)
    {
        var id = new FormulaId(formulaId);
        var formula = await formulas.GetByIdAsync(id, cancellationToken);
        if (formula is null)
        {
            return FormulaErrors.NotFound(id);
        }

        var parsedMaterialId = MaterialId.Create(materialId);
        if (parsedMaterialId.IsFailure)
        {
            return parsedMaterialId.Error;
        }

        var result = change(formula, parsedMaterialId.Value);
        if (result.IsFailure)
        {
            return result;
        }

        await formulas.SaveAsync(formula, cancellationToken);
        return Result.Success();
    }
}
