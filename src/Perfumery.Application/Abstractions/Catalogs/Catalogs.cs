using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;

namespace Perfumery.Application.Abstractions.Catalogs;

/// <summary>Raw botanicals, spirit and flacons the atelier can buy.</summary>
public interface ISupplyCatalog
{
    IReadOnlyList<Supply> GetAll();

    Supply? Find(SupplyId id);

    /// <summary>The one solvent: carrier for stock dilutions, tinctures and finished products.</summary>
    Supply Spirit { get; }
}

public interface IExtractionRecipeCatalog
{
    IReadOnlyList<ExtractionRecipe> GetAll();

    ExtractionRecipe? Find(ExtractionRecipeId id);
}

public interface IProductTypeCatalog
{
    IReadOnlyList<ProductType> GetAll();

    ProductType? Find(ProductTypeId id);
}
