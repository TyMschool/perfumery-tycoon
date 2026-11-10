using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

/// <summary>One material in a formula. Identified within its formula by the material's ID.</summary>
public sealed class FormulaLine : Entity<MaterialId>
{
    internal FormulaLine(MaterialId materialId, Parts parts, Dilution dilution)
        : base(materialId)
    {
        Parts = parts;
        Dilution = dilution;
    }

    public MaterialId MaterialId => Id;

    public Parts Parts { get; private set; }

    public Dilution Dilution { get; private set; }

    /// <summary>Parts of actual material once the stock solution's carrier is set aside.</summary>
    public decimal NeatParts => Parts.Value * Dilution.Fraction;

    internal void SetParts(Parts parts) => Parts = parts;

    internal void SetDilution(Dilution dilution) => Dilution = dilution;
}
