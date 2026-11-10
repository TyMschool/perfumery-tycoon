using FluentValidation;
using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Game;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Production.StartProductRun;

/// <param name="BatchId">A matured concentrate batch to draw from.</param>
/// <param name="ProductTypeId">Product type slug.</param>
/// <param name="ConcentrateGrams">Concentrate to draw.</param>
/// <param name="ConcentrationPercent">Strength of the product; null uses the product type's default.</param>
public sealed record StartProductRunCommand(
    Guid BatchId,
    string ProductTypeId,
    decimal ConcentrateGrams,
    decimal? ConcentrationPercent = null)
    : ICommand<Result<ProductRunId>>;

internal sealed class StartProductRunCommandValidator : AbstractValidator<StartProductRunCommand>
{
    public StartProductRunCommandValidator()
    {
        RuleFor(command => command.BatchId).NotEmpty();
        RuleFor(command => command.ProductTypeId).NotEmpty();
    }
}

internal sealed class StartProductRunCommandHandler(
    IGameSession session,
    IGameClock clock,
    IProductTypeCatalog productTypes,
    StockItemDirectory items,
    AtelierSettings settings)
    : ICommandHandler<StartProductRunCommand, Result<ProductRunId>>
{
    public Task<Result<ProductRunId>> Handle(StartProductRunCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(command));

    private Result<ProductRunId> Handle(StartProductRunCommand command)
    {
        if (!session.IsStarted)
        {
            return GameErrors.NotStarted;
        }

        var batch = session.Find(new ConcentrateBatchId(command.BatchId));
        if (batch is null)
        {
            return ProductionErrors.BatchNotFound(command.BatchId);
        }

        var productTypeId = ProductTypeId.Create(command.ProductTypeId);
        var mass = Mass.FromGrams(command.ConcentrateGrams);
        if (Result.FirstFailure(productTypeId, mass) is { } failure)
        {
            return failure;
        }

        var productType = productTypes.Find(productTypeId.Value);
        if (productType is null)
        {
            return ProductionErrors.ProductTypeNotFound(command.ProductTypeId);
        }

        var concentration = command.ConcentrationPercent is { } percent
            ? Concentration.FromPercent(percent)
            : productType.DefaultConcentration;
        if (concentration.IsFailure)
        {
            return concentration.Error;
        }

        var now = session.Atelier.Observe(clock.UtcNow);
        var run = BottlingService.StartRun(
            session.Inventory,
            batch,
            productType,
            concentration.Value,
            mass.Value,
            settings,
            session.EquipmentInUse(EquipmentKind.MacerationVessel),
            items.Spirit,
            now);
        if (run.IsFailure)
        {
            return run.Error;
        }

        session.Add(run.Value);
        return run.Value.Id;
    }
}
