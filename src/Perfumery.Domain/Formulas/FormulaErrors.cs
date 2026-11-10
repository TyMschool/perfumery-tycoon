using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

public static class FormulaErrors
{
    public static readonly Error NameRequired =
        Error.Validation("Formula.NameRequired", "A formula needs a name.");

    public static readonly Error NameTooLong =
        Error.Validation("Formula.NameTooLong", $"A formula name can be at most {Formula.MaxNameLength} characters.");

    public static readonly Error TooManyLines =
        Error.Conflict("Formula.TooManyLines", $"A formula can have at most {Formula.MaxLines} lines.");

    public static readonly Error EmptyFormula =
        Error.Validation("Formula.Empty", "A formula with no lines cannot be evaluated.");

    public static Error MaterialAlreadyPresent(MaterialId materialId) =>
        Error.Conflict(
            "Formula.MaterialAlreadyPresent",
            $"'{materialId.Value}' is already in the formula; adjust the existing line instead.");

    public static Error LineNotFound(MaterialId materialId) =>
        Error.NotFound("Formula.LineNotFound", $"The formula has no line for '{materialId.Value}'.");

    public static Error NotFound(FormulaId id) =>
        Error.NotFound("Formula.NotFound", $"No formula with ID '{id.Value}' exists.");
}
