using Perfumery.SharedKernel;

namespace Perfumery.Application.Abstractions.Game;

/// <summary>Reads and writes one named blob: a file on disk, or browser storage later.</summary>
public interface ISaveStore
{
    /// <summary>The saved text, or null if there is no save.</summary>
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the save. May be coalesced with later writes; see <see cref="FlushAsync"/>.</summary>
    Task WriteAsync(string content, CancellationToken cancellationToken);

    /// <summary>Writes anything still pending.</summary>
    Task FlushAsync(CancellationToken cancellationToken);

    /// <summary>Copies the current save aside before it is replaced by a new game.</summary>
    Task SetAsideAsync(CancellationToken cancellationToken);
}

/// <summary>Turns the game into a versioned save document and back.</summary>
public interface IGameSaver
{
    Task SaveAsync(GameSnapshot snapshot, DateTimeOffset savedAt, CancellationToken cancellationToken);

    /// <summary>The saved game, null if there is none, or a typed error if it cannot be read.</summary>
    Task<Result<GameSnapshot?>> LoadAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(CancellationToken cancellationToken);

    Task SetAsideAsync(CancellationToken cancellationToken);
}

public static class SaveErrors
{
    public static readonly Error Unreadable =
        Error.Failure("Save.Unreadable", "The save file is damaged and cannot be read.");

    public static Error NewerVersion(int found, int supported) =>
        Error.Failure(
            "Save.NewerVersion",
            $"The save is from a newer version of the game (schema {found}; this build reads up to {supported}).");

    public static Error Invalid(Error cause) =>
        Error.Failure("Save.Invalid", $"The save holds something that is not valid: {cause.Description}");

    public static Error UnknownReference(string id) =>
        Error.Failure("Save.UnknownReference", $"The save refers to '{id}', which this version of the game does not have.");
}

public static class GameErrors
{
    public static readonly Error NotStarted =
        Error.Conflict("Game.NotStarted", "No game is running. Start a new game or load one first.");

    public static readonly Error AlreadyStarted =
        Error.Conflict("Game.AlreadyStarted", "A game is already running.");
}
