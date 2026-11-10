using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

/// <summary>
/// A perfume concentrate formula: materials in parts by weight, each drawn from a stock
/// solution at some dilution. Percentages are always derived, never stored.
/// </summary>
public sealed class Formula : AggregateRoot<FormulaId>
{
    public const int MaxNameLength = 80;
    public const int MaxLines = 40;

    private readonly List<FormulaLine> _lines = [];

    private Formula(FormulaId id, string name)
        : base(id)
    {
        Name = name;
        Revision = 1;
    }

    public string Name { get; }

    /// <summary>Starts at 1 and goes up by one with every successful change.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<FormulaLine> Lines => _lines.AsReadOnly();

    public bool IsEmpty => _lines.Count == 0;

    public decimal TotalParts => _lines.Sum(line => line.Parts.Value);

    /// <summary>The carrier share of the concentrate, brought in by diluted stock solutions.</summary>
    public decimal SolventFraction => IsEmpty ? 0m : 1m - _lines.Sum(NeatFractionOf);

    public static Result<Formula> Create(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FormulaErrors.NameRequired;
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            return FormulaErrors.NameTooLong;
        }

        var formula = new Formula(FormulaId.New(), trimmed);
        formula.Raise(new FormulaCreatedDomainEvent(formula.Id, formula.Revision));
        return formula;
    }

    /// <summary>
    /// Adds a line. Takes the material itself so a line can never point at a material
    /// that doesn't exist. With no dilution given, the material's lab stock dilution is used.
    /// </summary>
    public Result AddLine(Material material, Parts parts, Dilution? dilution = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(parts);

        if (FindLine(material.Id) is not null)
        {
            return FormulaErrors.MaterialAlreadyPresent(material.Id);
        }

        if (_lines.Count >= MaxLines)
        {
            return FormulaErrors.TooManyLines;
        }

        _lines.Add(new FormulaLine(material.Id, parts, dilution ?? material.StockDilution));
        return Revised();
    }

    public Result AdjustParts(MaterialId materialId, Parts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var line = FindLine(materialId);
        if (line is null)
        {
            return FormulaErrors.LineNotFound(materialId);
        }

        if (line.Parts == parts)
        {
            return Result.Success();
        }

        line.SetParts(parts);
        return Revised();
    }

    public Result ChangeDilution(MaterialId materialId, Dilution dilution)
    {
        ArgumentNullException.ThrowIfNull(dilution);

        var line = FindLine(materialId);
        if (line is null)
        {
            return FormulaErrors.LineNotFound(materialId);
        }

        if (line.Dilution == dilution)
        {
            return Result.Success();
        }

        line.SetDilution(dilution);
        return Revised();
    }

    public Result RemoveLine(MaterialId materialId)
    {
        var line = FindLine(materialId);
        if (line is null)
        {
            return FormulaErrors.LineNotFound(materialId);
        }

        _lines.Remove(line);
        return Revised();
    }

    /// <summary>
    /// How much actual material a line puts in the concentrate:
    /// parts × dilution ÷ total parts.
    /// </summary>
    public Result<decimal> NeatFraction(MaterialId materialId)
    {
        var line = FindLine(materialId);
        return line is null ? FormulaErrors.LineNotFound(materialId) : NeatFractionOf(line);
    }

    /// <summary>Freezes the formula as it stands into an immutable composition.</summary>
    public Result<Composition> Compose() =>
        IsEmpty
            ? FormulaErrors.EmptyFormula
            : Composition.Create(_lines.Select(line => new CompositionLine(line.MaterialId, NeatFractionOf(line))));

    /// <summary>Points at the current revision.</summary>
    public FormulaRevisionRef CurrentRevision => FormulaRevisionRef.Create(Id, Revision).Value;

    public FormulaSnapshot ToSnapshot() =>
        new(
            Id.Value,
            Name,
            Revision,
            [.. _lines.Select(line => new FormulaLineSnapshot(line.MaterialId.Value, line.Parts.Value, line.Dilution.Fraction))]);

    /// <summary>Rebuilds a formula from saved state, re-checking every invariant. Raises no events.</summary>
    public static Result<Formula> Restore(FormulaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.Name))
        {
            return FormulaErrors.NameRequired;
        }

        if (snapshot.Name.Length > MaxNameLength)
        {
            return FormulaErrors.NameTooLong;
        }

        if (snapshot.Revision < 1)
        {
            return FormulaErrors.InvalidRevision;
        }

        if (snapshot.Lines.Count > MaxLines)
        {
            return FormulaErrors.TooManyLines;
        }

        var formula = new Formula(new FormulaId(snapshot.Id), snapshot.Name) { Revision = snapshot.Revision };
        foreach (var line in snapshot.Lines)
        {
            var materialId = MaterialId.Create(line.MaterialId);
            if (materialId.IsFailure)
            {
                return materialId.Error;
            }

            var parts = Parts.Create(line.Parts);
            if (parts.IsFailure)
            {
                return parts.Error;
            }

            var dilution = Dilution.FromFraction(line.DilutionFraction);
            if (dilution.IsFailure)
            {
                return dilution.Error;
            }

            if (formula.FindLine(materialId.Value) is not null)
            {
                return FormulaErrors.MaterialAlreadyPresent(materialId.Value);
            }

            formula._lines.Add(new FormulaLine(materialId.Value, parts.Value, dilution.Value));
        }

        return formula;
    }

    private decimal NeatFractionOf(FormulaLine line) => line.NeatParts / TotalParts;

    private FormulaLine? FindLine(MaterialId materialId) =>
        _lines.Find(line => line.MaterialId == materialId);

    private Result Revised()
    {
        Revision++;
        Raise(new FormulaRevisedDomainEvent(Id, Revision));
        return Result.Success();
    }
}
