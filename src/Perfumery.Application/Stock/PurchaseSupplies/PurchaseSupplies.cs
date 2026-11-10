using FluentValidation;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Game;
using Perfumery.Domain.Production;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Stock.PurchaseSupplies;

/// <param name="Item">Stock item ID, e.g. <c>material:bergamot-calabria</c> or <c>supply:spirit-of-wine</c>.</param>
/// <param name="Quantity">Kilograms, or a whole number of pieces for items sold by the piece.</param>
public sealed record PurchaseSuppliesCommand(string Item, decimal Quantity) : ICommand;

internal sealed class PurchaseSuppliesCommandValidator : AbstractValidator<PurchaseSuppliesCommand>
{
    public PurchaseSuppliesCommandValidator()
    {
        RuleFor(command => command.Item).NotEmpty();
    }
}

internal sealed class PurchaseSuppliesCommandHandler(IGameSession session, IGameClock clock, StockItemDirectory items)
    : ICommandHandler<PurchaseSuppliesCommand, Result>
{
    public Task<Result> Handle(PurchaseSuppliesCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(command));

    private Result Handle(PurchaseSuppliesCommand command)
    {
        if (!session.IsStarted)
        {
            return GameErrors.NotStarted;
        }

        var item = StockItemId.Parse(command.Item);
        var quantity = Quantity.Create(command.Quantity);
        if (Result.FirstFailure(item, quantity) is { } failure)
        {
            return failure;
        }

        var info = items.Find(item.Value);
        if (info is null)
        {
            return StockErrors.ItemNotFound(item.Value);
        }

        if (info.Unit == StockUnit.Each && !quantity.Value.IsWhole)
        {
            return StockErrors.QuantityNotWhole;
        }

        var now = session.Atelier.Observe(clock.UtcNow);
        return PurchasingService.Purchase(session.Atelier, session.Inventory, item.Value, quantity.Value, info.Price, now);
    }
}
