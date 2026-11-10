using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Game.LoadGame;

/// <summary>Reads the save, if there is one, and makes it the running game.</summary>
public sealed record LoadGameCommand : ICommand<Result<LoadGameOutcome>>;

public enum LoadGameOutcome
{
    /// <summary>The saved game is now running.</summary>
    Loaded,

    /// <summary>There is no save; start a new game.</summary>
    NoSave,
}

internal sealed class LoadGameCommandHandler(IGameSession session, IGameSaver saver, GameRestorer restorer)
    : ICommandHandler<LoadGameCommand, Result<LoadGameOutcome>>
{
    public async Task<Result<LoadGameOutcome>> Handle(LoadGameCommand command, CancellationToken cancellationToken)
    {
        var loaded = await saver.LoadAsync(cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        if (loaded.Value is null)
        {
            return LoadGameOutcome.NoSave;
        }

        var state = restorer.Restore(loaded.Value);
        if (state.IsFailure)
        {
            return state.Error;
        }

        session.Load(state.Value);
        return LoadGameOutcome.Loaded;
    }
}
