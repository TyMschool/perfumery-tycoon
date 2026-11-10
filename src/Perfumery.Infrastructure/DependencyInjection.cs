using Microsoft.Extensions.DependencyInjection;
using Perfumery.Application.Abstractions.Materials;
using Perfumery.Application.Abstractions.Persistence;
using Perfumery.Domain.Evaluation;
using Perfumery.Infrastructure.Evaluation;
using Perfumery.Infrastructure.Materials;
using Perfumery.Infrastructure.Persistence;

namespace Perfumery.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IMaterialCatalog, JsonMaterialCatalog>();
        services.AddSingleton<IFormulaRepository, InMemoryFormulaRepository>();
        services.AddSingleton(_ => EvaluationSettingsLoader.Load());
        services.AddSingleton<ScentEvaluator>();

        return services;
    }
}
