namespace Perfumery.Domain.Formulas;

public readonly record struct FormulaId(Guid Value)
{
    public static FormulaId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
