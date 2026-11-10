using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Domain.Formulas;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Stock.GetShelf;

public sealed record GetShelfQuery : IQuery<Result<IReadOnlyList<ShelfLotResponse>>>;

/// <param name="ProductRunId">The run the units came from.</param>
/// <param name="FormulaId">The formula.</param>
/// <param name="FormulaName">Its current name.</param>
/// <param name="FormulaRevision">The revision that was bottled.</param>
/// <param name="ProductTypeId">Product type slug.</param>
/// <param name="ProductName">Product type name.</param>
/// <param name="ConcentrationPercent">Strength of the product.</param>
/// <param name="Units">Flacons on the shelf.</param>
/// <param name="UnitCost">What each cost to make, to 4 decimal places.</param>
/// <param name="BottledAt">When they were bottled.</param>
public sealed record ShelfLotResponse(
    Guid ProductRunId,
    Guid FormulaId,
    string FormulaName,
    int FormulaRevision,
    string ProductTypeId,
    string ProductName,
    decimal ConcentrationPercent,
    int Units,
    decimal UnitCost,
    DateTimeOffset BottledAt);

internal sealed class GetShelfQueryHandler(IGameSession session, IProductTypeCatalog productTypes)
    : IQueryHandler<GetShelfQuery, Result<IReadOnlyList<ShelfLotResponse>>>
{
    public Task<Result<IReadOnlyList<ShelfLotResponse>>> Handle(GetShelfQuery query, CancellationToken cancellationToken)
    {
        if (!session.IsStarted)
        {
            return Task.FromResult<Result<IReadOnlyList<ShelfLotResponse>>>(GameErrors.NotStarted);
        }

        IReadOnlyList<ShelfLotResponse> lots =
        [
            .. session.Inventory.Shelf.Select(lot => new ShelfLotResponse(
                lot.ProductRunId.Value,
                lot.Formula.FormulaId.Value,
                session.Find(lot.Formula.FormulaId)?.Name ?? string.Empty,
                lot.Formula.Revision,
                lot.ProductTypeId.Value,
                productTypes.Find(lot.ProductTypeId)?.Name ?? lot.ProductTypeId.Value,
                lot.Concentration.Percent,
                lot.Units,
                Math.Round(lot.UnitCost.Amount, 4, MidpointRounding.AwayFromZero),
                lot.BottledAt)),
        ];

        return Task.FromResult(Result.Success(lots));
    }
}
