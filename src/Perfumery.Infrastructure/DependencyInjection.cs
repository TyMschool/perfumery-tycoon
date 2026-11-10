using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Perfumery.Application.Abstractions.Catalogs;
using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Evaluation;
using Perfumery.Infrastructure.Ateliers;
using Perfumery.Infrastructure.Evaluation;
using Perfumery.Infrastructure.Game;
using Perfumery.Infrastructure.Materials;
using Perfumery.Infrastructure.Persistence;
using Perfumery.Infrastructure.Production;
using Perfumery.Infrastructure.Saves;
using Perfumery.Infrastructure.Stock;

namespace Perfumery.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers reference data, the in-memory game session, the clock and the save file.
    /// Register a <see cref="TimeProvider"/> first to replace the system clock.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<SaveOptions>? configure = null)
    {
        var options = new SaveOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IGameClock, SystemGameClock>();

        services.AddSingleton<IMaterialCatalog, JsonMaterialCatalog>();
        services.AddSingleton<ISupplyCatalog, JsonSupplyCatalog>();
        services.AddSingleton<IExtractionRecipeCatalog, JsonExtractionRecipeCatalog>();
        services.AddSingleton<IProductTypeCatalog, JsonProductTypeCatalog>();
        services.AddSingleton(_ => EvaluationSettingsLoader.Load());
        services.AddSingleton(_ => AtelierSettingsLoader.Load(options.TimeScale));
        services.AddSingleton<ScentEvaluator>();

        services.AddSingleton<IGameSession, InMemoryGameSession>();
        services.AddSingleton<IFormulaRepository, InMemoryFormulaRepository>();

        services.AddSingleton(options);
        if (options.InMemory)
        {
            services.AddSingleton<ISaveStore, InMemorySaveStore>();
        }
        else
        {
            services.AddSingleton<ISaveStore, FileSaveStore>();
        }

        services.AddSingleton<IGameSaver, JsonGameSaver>();

        return services;
    }
}
