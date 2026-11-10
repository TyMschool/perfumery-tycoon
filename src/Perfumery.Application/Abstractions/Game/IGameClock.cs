namespace Perfumery.Application.Abstractions.Game;

/// <summary>
/// The only way the application reads the time. Handlers pass what it returns through
/// the atelier, which refuses to let time run backwards.
/// </summary>
public interface IGameClock
{
    DateTimeOffset UtcNow { get; }
}
