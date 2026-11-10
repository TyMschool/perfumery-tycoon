using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Abstractions.Behaviors;

/// <summary>
/// Writes the whole game after every successful command. Failed commands write nothing,
/// and queries are never wrapped in this.
/// </summary>
internal sealed class SaveGameDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IGameSession session,
    IGameSaver saver,
    IGameClock clock)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var result = await inner.Handle(command, cancellationToken);
        if (result.IsSuccess && session.IsStarted)
        {
            session.PruneFinished();
            await saver.SaveAsync(session.ToSnapshot(), session.Atelier.EffectiveNow(clock.UtcNow), cancellationToken);
        }

        return result;
    }
}
