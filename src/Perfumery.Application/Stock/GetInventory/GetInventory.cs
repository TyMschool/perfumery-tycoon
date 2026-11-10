using FluentValidation;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Game;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Stock.GetInventory;

/// <param name="Kind">Only materials or only supplies; null returns everything.</param>
public sealed record GetInventoryQuery(StockItemKind? Kind = null) : IQuery<Result<IReadOnlyList<StockLineResponse>>>;

/// <param name="Item">Stock item ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="Unit">Kilogram or Each.</param>
/// <param name="Quantity">Amount held, in that unit.</param>
/// <param name="AverageUnitCost">Weighted-average cost per unit, to 4 decimal places.</param>
/// <param name="TotalCost">What the stock held had cost.</param>
public sealed record StockLineResponse(
    string Item,
    string Name,
    string Unit,
    decimal Quantity,
    decimal AverageUnitCost,
    decimal TotalCost);

internal sealed class GetInventoryQueryValidator : AbstractValidator<GetInventoryQuery>
{
    public GetInventoryQueryValidator()
    {
        RuleFor(query => query.Kind).IsInEnum();
    }
}

internal sealed class GetInventoryQueryHandler(IGameSession session, StockItemDirectory items)
    : IQueryHandler<GetInventoryQuery, Result<IReadOnlyList<StockLineResponse>>>
{
    public Task<Result<IReadOnlyList<StockLineResponse>>> Handle(GetInventoryQuery query, CancellationToken cancellationToken)
    {
        if (!session.IsStarted)
        {
            return Task.FromResult<Result<IReadOnlyList<StockLineResponse>>>(GameErrors.NotStarted);
        }

        IReadOnlyList<StockLineResponse> lines =
        [
            .. session.Inventory.Lines
                .Where(line => query.Kind is null || line.Item.Kind == query.Kind)
                .Select(line =>
                {
                    var info = items.Find(line.Item);
                    return new StockLineResponse(
                        line.Item.ToString(),
                        info?.Name ?? line.Item.Slug,
                        (info?.Unit ?? StockUnit.Kilogram).ToString(),
                        line.Quantity.Value,
                        Math.Round(line.AverageUnitCost.Amount, 4, MidpointRounding.AwayFromZero),
                        line.TotalCost.ToCentimes().Amount);
                }),
        ];

        return Task.FromResult(Result.Success(lines));
    }
}
