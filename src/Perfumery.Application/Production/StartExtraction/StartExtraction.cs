using FluentValidation;
using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Game;
using Perfumery.Domain.Ateliers;
using Perfumery.Domain.Common;
using Perfumery.Domain.Production;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Production.StartExtraction;

/// <param name="RecipeId">Extraction recipe slug.</param>
/// <param name="RawGrams">Raw botanical to charge, in grams.</param>
public sealed record StartExtractionCommand(string RecipeId, decimal RawGrams) : ICommand<Result<ExtractionRunId>>;

internal sealed class StartExtractionCommandValidator : AbstractValidator<StartExtractionCommand>
{
    public StartExtractionCommandValidator()
    {
        RuleFor(command => command.RecipeId).NotEmpty();
    }
}

internal sealed class StartExtractionCommandHandler(
    IGameSession session,
    IGameClock clock,
    IExtractionRecipeCatalog recipes,
    StockItemDirectory items,
    AtelierSettings settings)
    : ICommandHandler<StartExtractionCommand, Result<ExtractionRunId>>
{
    public Task<Result<ExtractionRunId>> Handle(StartExtractionCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(command));

    private Result<ExtractionRunId> Handle(StartExtractionCommand command)
    {
        if (!session.IsStarted)
        {
            return GameErrors.NotStarted;
        }

        var recipeId = ExtractionRecipeId.Create(command.RecipeId);
        var rawMass = Mass.FromGrams(command.RawGrams);
        if (Result.FirstFailure(recipeId, rawMass) is { } failure)
        {
            return failure;
        }

        var recipe = recipes.Find(recipeId.Value);
        if (recipe is null)
        {
            return ExtractionErrors.RecipeNotFound(command.RecipeId);
        }

        var now = session.Atelier.Observe(clock.UtcNow);
        var run = ExtractionService.Start(
            session.Inventory,
            recipe,
            rawMass.Value,
            settings,
            session.EquipmentInUse(recipe.Equipment),
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
