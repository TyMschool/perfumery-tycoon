using FluentValidation;
using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Game;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Production.QuoteProduction;

/// <summary>
/// A dry run of compounding a formula and, if a product type is given, of diluting and
/// bottling the whole batch. Changes nothing.
/// </summary>
/// <param name="FormulaId">The formula, at its current revision.</param>
/// <param name="Grams">Mass of concentrate.</param>
/// <param name="ProductTypeId">Optional product to take the whole batch through to bottles.</param>
/// <param name="ConcentrationPercent">Strength of the product; null uses the product type's default.</param>
public sealed record QuoteProductionQuery(
    Guid FormulaId,
    decimal Grams,
    string? ProductTypeId = null,
    decimal? ConcentrationPercent = null)
    : IQuery<Result<ProductionQuoteResponse>>;

/// <param name="Requirements">Everything the work takes from stock, merged per item.</param>
/// <param name="CanProduce">True when nothing is short.</param>
/// <param name="CostToBuyMissing">Catalog cost of every shortage together.</param>
/// <param name="ConcentrateGrams">Mass of the batch.</param>
/// <param name="ConcentrateCost">Cost of the batch: stock on hand at its average cost, shortages at catalog price.</param>
/// <param name="MaturationSeconds">How long the batch matures.</param>
/// <param name="Product">The product run, when a product type was given.</param>
public sealed record ProductionQuoteResponse(
    IReadOnlyList<QuoteRequirementResponse> Requirements,
    bool CanProduce,
    decimal CostToBuyMissing,
    decimal ConcentrateGrams,
    decimal ConcentrateCost,
    double MaturationSeconds,
    ProductQuoteResponse? Product);

public sealed record QuoteRequirementResponse(
    string Item,
    string Name,
    string Unit,
    decimal Required,
    decimal Available,
    decimal Missing,
    decimal CostToBuyMissing);

public sealed record ProductQuoteResponse(
    string ProductTypeId,
    string ProductName,
    decimal ConcentrationPercent,
    decimal ProductGrams,
    int Units,
    decimal LossGrams,
    double MacerationSeconds,
    decimal TotalCost,
    decimal UnitCost);

internal sealed class QuoteProductionQueryValidator : AbstractValidator<QuoteProductionQuery>
{
    public QuoteProductionQueryValidator()
    {
        RuleFor(query => query.FormulaId).NotEmpty();
    }
}

internal sealed class QuoteProductionQueryHandler(
    IGameSession session,
    IProductTypeCatalog productTypes,
    StockItemDirectory items,
    AtelierSettings settings)
    : IQueryHandler<QuoteProductionQuery, Result<ProductionQuoteResponse>>
{
    public Task<Result<ProductionQuoteResponse>> Handle(QuoteProductionQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(query));

    private Result<ProductionQuoteResponse> Handle(QuoteProductionQuery query)
    {
        if (!session.IsStarted)
        {
            return GameErrors.NotStarted;
        }

        var formulaId = new FormulaId(query.FormulaId);
        var formula = session.Find(formulaId);
        if (formula is null)
        {
            return FormulaErrors.NotFound(formulaId);
        }

        var composition = formula.Compose();
        var mass = Mass.FromGrams(query.Grams);
        if (Result.FirstFailure(composition, mass) is { } failure)
        {
            return failure;
        }

        var plan = CompoundingService.Plan(composition.Value, mass.Value, items.Spirit);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var compounding = plan.Value.Requirements;
        var later = new List<StockRequirement>();
        ProductType? productType = null;
        Concentration? concentration = null;
        BottlingPlan? bottling = null;
        var productMass = Mass.Zero;

        if (query.ProductTypeId is not null)
        {
            var productTypeId = ProductTypeId.Create(query.ProductTypeId);
            if (productTypeId.IsFailure)
            {
                return productTypeId.Error;
            }

            productType = productTypes.Find(productTypeId.Value);
            if (productType is null)
            {
                return ProductionErrors.ProductTypeNotFound(query.ProductTypeId);
            }

            var strength = query.ConcentrationPercent is { } percent
                ? Concentration.FromPercent(percent)
                : productType.DefaultConcentration;
            var vessel = settings.Equipment[EquipmentKind.MacerationVessel].Capacity;
            if (Result.FirstFailure(strength) is { } invalid)
            {
                return invalid;
            }

            var check = ProductRun.Check(productType, strength.Value, plan.Value.Mass, vessel);
            if (check.IsFailure)
            {
                return check.Error;
            }

            concentration = strength.Value;
            productMass = ProductRun.ProductMassFor(plan.Value.Mass, concentration);
            bottling = ProductRun.PlanBottling(productMass, settings.FilterLoss, productType.FlaconFill);
            var units = bottling.Units;

            later.Add(BottlingService.DilutionSpirit(plan.Value.Mass, concentration, items.Spirit));
            if (units > 0)
            {
                later.Add(new StockRequirement(StockItemId.For(productType.Flacon), Quantity.Units(units)));
            }
        }

        // One blended unit cost per item: what is on hand at its average, the rest at catalog price.
        var totals = compounding.Concat(later)
            .GroupBy(requirement => requirement.Item)
            .ToDictionary(group => group.Key, group => group.Sum(requirement => requirement.Quantity.Value));

        var lines = new List<QuoteRequirementResponse>();
        var unitCosts = new Dictionary<StockItemId, decimal>();
        foreach (var (item, required) in totals)
        {
            var info = items.Find(item);
            if (info is null)
            {
                return StockErrors.ItemNotFound(item);
            }

            var onHand = session.Inventory.QuantityOf(item).Value;
            var used = Math.Min(required, onHand);
            var missing = required - used;
            var cost = (used * session.Inventory.AverageUnitCost(item).Amount) + (missing * info.Price.Amount);
            unitCosts[item] = required == 0m ? 0m : cost / required;

            lines.Add(new QuoteRequirementResponse(
                item.ToString(),
                info.Name,
                info.Unit.ToString(),
                required,
                onHand,
                missing,
                Round(missing * info.Price.Amount)));
        }

        decimal CostOf(IEnumerable<StockRequirement> requirements) =>
            requirements.Sum(requirement => requirement.Quantity.Value * unitCosts[requirement.Item]);

        var concentrateCost = CostOf(compounding);
        ProductQuoteResponse? product = null;
        if (productType is not null && concentration is not null && bottling is not null)
        {
            var total = concentrateCost + CostOf(later);
            product = new ProductQuoteResponse(
                productType.Id.Value,
                productType.Name,
                concentration.Percent,
                productMass.Grams,
                bottling.Units,
                bottling.Loss.Grams,
                settings.Scale(productType.Maceration).TotalSeconds,
                Round(total),
                bottling.Units > 0 ? Math.Round(total / bottling.Units, 4, MidpointRounding.AwayFromZero) : 0m);
        }

        return new ProductionQuoteResponse(
            [.. lines.OrderBy(line => line.Item, StringComparer.Ordinal)],
            lines.TrueForAll(line => line.Missing == 0m),
            lines.Sum(line => line.CostToBuyMissing),
            plan.Value.Mass.Grams,
            Round(concentrateCost),
            settings.Scale(settings.ConcentrateMaturation).TotalSeconds,
            product);
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
