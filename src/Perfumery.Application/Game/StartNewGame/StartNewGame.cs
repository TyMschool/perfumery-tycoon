using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Stock;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Game.StartNewGame;

/// <summary>
/// Opens a fresh atelier with the era's starting capital. If a save exists that was not
/// loaded, it is copied aside first and never silently lost.
/// </summary>
public sealed record StartNewGameCommand : ICommand;

internal sealed class StartNewGameCommandHandler(
    IGameSession session,
    IGameSaver saver,
    IGameClock clock,
    AtelierSettings settings)
    : ICommandHandler<StartNewGameCommand, Result>
{
    public async Task<Result> Handle(StartNewGameCommand command, CancellationToken cancellationToken)
    {
        if (session.IsStarted)
        {
            return GameErrors.AlreadyStarted;
        }

        if (await saver.ExistsAsync(cancellationToken))
        {
            await saver.SetAsideAsync(cancellationToken);
        }

        session.Start(Atelier.Open(settings.StartingCapital, clock.UtcNow), Inventory.Create());
        return Result.Success();
    }
}
