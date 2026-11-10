using FluentValidation;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Production.CollectExtraction;

public sealed record CollectExtractionCommand(Guid RunId) : ICommand;

internal sealed class CollectExtractionCommandValidator : AbstractValidator<CollectExtractionCommand>
{
    public CollectExtractionCommandValidator()
    {
        RuleFor(command => command.RunId).NotEmpty();
    }
}

internal sealed class CollectExtractionCommandHandler(IGameSession session, IGameClock clock)
    : ICommandHandler<CollectExtractionCommand, Result>
{
    public Task<Result> Handle(CollectExtractionCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(command));

    private Result Handle(CollectExtractionCommand command)
    {
        if (!session.IsStarted)
        {
            return GameErrors.NotStarted;
        }

        var run = session.Find(new ExtractionRunId(command.RunId));
        if (run is null)
        {
            return ExtractionErrors.RunNotFound(command.RunId);
        }

        var now = session.Atelier.Observe(clock.UtcNow);
        return ExtractionService.Collect(run, session.Inventory, now);
    }
}
