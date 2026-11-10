using Perfumery.Domain.Common;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Stock;

public enum StockItemKind
{
    Material,
    Supply,
}

/// <summary>
/// Anything the inventory holds, written as kind plus slug, e.g.
/// <c>material:lavender-haute-provence</c> or <c>supply:spirit-of-wine</c>.
/// </summary>
public sealed record StockItemId
{
    private StockItemId(StockItemKind kind, string slug)
    {
        Kind = kind;
        Slug = slug;
    }

    public StockItemKind Kind { get; }

    public string Slug { get; }

    public static StockItemId For(MaterialId material) => new(StockItemKind.Material, material.Value);

    public static StockItemId For(SupplyId supply) => new(StockItemKind.Supply, supply.Value);

    public static Result<StockItemId> Parse(string? value)
    {
        var parts = value?.Split(':');
        if (parts is not [var kind, var slug] || !Common.Slug.IsValid(slug))
        {
            return StockErrors.ItemIdMalformed;
        }

        return kind switch
        {
            "material" => new StockItemId(StockItemKind.Material, slug),
            "supply" => new StockItemId(StockItemKind.Supply, slug),
            _ => StockErrors.ItemIdMalformed,
        };
    }

    public override string ToString() => $"{(Kind == StockItemKind.Material ? "material" : "supply")}:{Slug}";
}
