using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

/// <summary>Points at one revision of a formula.</summary>
public sealed record FormulaRevisionRef
{
    private FormulaRevisionRef(FormulaId formulaId, int revision)
    {
        FormulaId = formulaId;
        Revision = revision;
    }

    public FormulaId FormulaId { get; }

    public int Revision { get; }

    public static Result<FormulaRevisionRef> Create(FormulaId formulaId, int revision) =>
        revision < 1 ? FormulaErrors.InvalidRevision : new FormulaRevisionRef(formulaId, revision);
}
