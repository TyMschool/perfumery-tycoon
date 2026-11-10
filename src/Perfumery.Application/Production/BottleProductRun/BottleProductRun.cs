using FluentValidation;
using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Production.BottleProductRun;

public sealed record BottleProductRunCommand(Guid RunId) : ICommand;

internal sealed class BottleProductRunCommandValidator : AbstractValidator<BottleProductRunCommand>
{
    public BottleProductRunCommandValidator()
    {
        RuleFor(command => command.RunId).NotEmpty();
    }
}

internal sealed class BottleProductRunCommandHandler(
    IGameSession session,
    IGameClock clock,
    IProductTypeCatalog productTypes,
    AtelierSettings settings)
    : ICommandHandler<BottleProductRunCommand, Result>
{
    public Task<Result> Handle(BottleProductRunCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(command));

    private Result Handle(BottleProductRunCommand command)
    {
        if (!session.IsStarted)
        {
            return GameErrors.NotStarted;
        }

        var run = session.Find(new ProductRunId(command.RunId));
        if (run is null)
        {
            return ProductionErrors.RunNotFound(command.RunId);
        }

        var productType = productTypes.Find(run.ProductTypeId);
        if (productType is null)
        {
            return ProductionErrors.ProductTypeNotFound(run.ProductTypeId.Value);
        }

        var now = session.Atelier.Observe(clock.UtcNow);
        return BottlingService.Bottle(run, session.Inventory, productType, settings, now);
    }
}
