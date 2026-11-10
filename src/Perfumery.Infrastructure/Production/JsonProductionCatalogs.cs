using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Materials;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;

namespace Perfumery.Infrastructure.Production;

internal sealed class JsonExtractionRecipeCatalog : IExtractionRecipeCatalog
{
    private const string Era1ResourceName = "Perfumery.Infrastructure.Production.extraction.era1.json";

    private readonly IReadOnlyList<ExtractionRecipe> _recipes;
    private readonly Dictionary<ExtractionRecipeId, ExtractionRecipe> _byId;

    public JsonExtractionRecipeCatalog(IMaterialCatalog materials, ISupplyCatalog supplies)
        : this(EmbeddedResources.Open(Era1ResourceName), Era1ResourceName, materials, supplies)
    {
    }

    internal JsonExtractionRecipeCatalog(Stream json, string sourceName, IMaterialCatalog materials, ISupplyCatalog supplies)
    {
        _recipes = [.. ReferenceData.LoadArray<RecipeDto>(json, sourceName).Select(dto => ToRecipe(dto, sourceName))];
        _byId = ReferenceData.Index(_recipes, recipe => recipe.Id, sourceName);

        foreach (var recipe in _recipes)
        {
            if (materials.Find(recipe.Output) is null)
            {
                throw new InvalidOperationException($"{sourceName}: '{recipe.Id}' makes unknown material '{recipe.Output}'.");
            }

            if (supplies.Find(recipe.RawBotanical) is not { Kind: SupplyKind.RawBotanical })
            {
                throw new InvalidOperationException($"{sourceName}: '{recipe.Id}' uses '{recipe.RawBotanical}', which is not a raw botanical.");
            }
        }
    }

    public IReadOnlyList<ExtractionRecipe> GetAll() => _recipes;

    public ExtractionRecipe? Find(ExtractionRecipeId id) => _byId.GetValueOrDefault(id);

    private static ExtractionRecipe ToRecipe(RecipeDto dto, string source) =>
        ReferenceData.Require(
            ExtractionRecipe.Create(
                ReferenceData.Require(ExtractionRecipeId.Create(dto.Id), source, dto.Id),
                ReferenceData.Require(MaterialId.Create(dto.Output), source, dto.Id),
                dto.Method,
                ReferenceData.Require(SupplyId.Create(dto.RawBotanical), source, dto.Id),
                dto.SpiritPerKgRaw,
                ReferenceData.Require(Yield.Create(dto.YieldKgPerKgRaw), source, dto.Id),
                TimeSpan.FromMinutes(dto.DurationMinutes),
                dto.Equipment),
            source,
            dto.Id);

    private sealed record RecipeDto(
        string Id,
        string Output,
        ExtractionMethod Method,
        string RawBotanical,
        decimal SpiritPerKgRaw,
        decimal YieldKgPerKgRaw,
        double DurationMinutes,
        EquipmentKind Equipment);
}

internal sealed class JsonProductTypeCatalog : IProductTypeCatalog
{
    private const string Era1ResourceName = "Perfumery.Infrastructure.Production.products.era1.json";

    private readonly IReadOnlyList<ProductType> _productTypes;
    private readonly Dictionary<ProductTypeId, ProductType> _byId;

    public JsonProductTypeCatalog(ISupplyCatalog supplies)
        : this(EmbeddedResources.Open(Era1ResourceName), Era1ResourceName, supplies)
    {
    }

    internal JsonProductTypeCatalog(Stream json, string sourceName, ISupplyCatalog supplies)
    {
        _productTypes = [.. ReferenceData.LoadArray<ProductTypeDto>(json, sourceName).Select(dto => ToProductType(dto, sourceName))];
        _byId = ReferenceData.Index(_productTypes, productType => productType.Id, sourceName);

        foreach (var productType in _productTypes)
        {
            if (supplies.Find(productType.Flacon) is not { Kind: SupplyKind.Packaging, Unit: StockUnit.Each })
            {
                throw new InvalidOperationException($"{sourceName}: '{productType.Id}' uses '{productType.Flacon}', which is not a flacon.");
            }
        }
    }

    public IReadOnlyList<ProductType> GetAll() => _productTypes;

    public ProductType? Find(ProductTypeId id) => _byId.GetValueOrDefault(id);

    private static ProductType ToProductType(ProductTypeDto dto, string source) =>
        ReferenceData.Require(
            ProductType.Create(
                ReferenceData.Require(ProductTypeId.Create(dto.Id), source, dto.Id),
                dto.Name,
                ReferenceData.Require(Concentration.FromPercent(dto.MinConcentrationPercent), source, dto.Id),
                ReferenceData.Require(Concentration.FromPercent(dto.MaxConcentrationPercent), source, dto.Id),
                ReferenceData.Require(Concentration.FromPercent(dto.DefaultConcentrationPercent), source, dto.Id),
                TimeSpan.FromMinutes(dto.MacerationMinutes),
                ReferenceData.Require(SupplyId.Create(dto.Flacon), source, dto.Id),
                ReferenceData.Require(Mass.FromGrams(dto.FlaconFillGrams), source, dto.Id)),
            source,
            dto.Id);

    private sealed record ProductTypeDto(
        string Id,
        string Name,
        decimal MinConcentrationPercent,
        decimal MaxConcentrationPercent,
        decimal DefaultConcentrationPercent,
        double MacerationMinutes,
        string Flacon,
        decimal FlaconFillGrams);
}
