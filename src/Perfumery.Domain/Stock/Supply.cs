using Perfumery.Domain.Common;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Stock;

/// <summary>Stable slug identifying a supply, e.g. <c>spirit-of-wine</c>.</summary>
public sealed record SupplyId
{
    private SupplyId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<SupplyId> Create(string? value) =>
        Slug.IsValid(value) ? new SupplyId(value!) : SupplyErrors.IdMalformed;

    public override string ToString() => Value;
}

public enum SupplyKind
{
    RawBotanical,
    Solvent,
    Packaging,
}

public enum StockUnit
{
    Kilogram,
    Each,
}

/// <summary>Something the atelier buys that is not a perfumery material: raw botanicals, spirit, flacons.</summary>
public sealed class Supply : Entity<SupplyId>
{
    private Supply(SupplyId id, string name, SupplyKind kind, StockUnit unit, Money price)
        : base(id)
    {
        Name = name;
        Kind = kind;
        Unit = unit;
        Price = price;
    }

    public string Name { get; }

    public SupplyKind Kind { get; }

    public StockUnit Unit { get; }

    /// <summary>Catalog price per unit.</summary>
    public Money Price { get; }

    public static Result<Supply> Create(SupplyId id, string name, SupplyKind kind, StockUnit unit, Money price)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SupplyErrors.NameRequired;
        }

        if (!Enum.IsDefined(kind) || !Enum.IsDefined(unit))
        {
            return SupplyErrors.UnknownKindOrUnit;
        }

        return new Supply(id, name.Trim(), kind, unit, price);
    }
}

public static class SupplyErrors
{
    public static readonly Error IdMalformed =
        Error.Validation("Supply.IdMalformed", "A supply ID must be lowercase words joined by hyphens.");

    public static readonly Error NameRequired = Error.Validation("Supply.NameRequired", "A supply needs a name.");

    public static readonly Error UnknownKindOrUnit =
        Error.Validation("Supply.UnknownKindOrUnit", "The supply kind or unit is not recognized.");

    public static Error NotFound(string id) =>
        Error.NotFound("Supply.NotFound", $"No supply with ID '{id}' exists in the catalog.");
}
