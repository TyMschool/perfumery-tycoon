namespace Perfumery.Domain.Formulas;

public sealed record FormulaSnapshot(Guid Id, string Name, int Revision, IReadOnlyList<FormulaLineSnapshot> Lines);

public sealed record FormulaLineSnapshot(string MaterialId, decimal Parts, decimal DilutionFraction);
