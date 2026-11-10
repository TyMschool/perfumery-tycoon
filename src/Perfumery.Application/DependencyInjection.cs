using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Perfumery.Application.Abstractions.Behaviors;
using Perfumery.Application.Abstractions.Messaging;

namespace Perfumery.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers every handler and validator in this assembly, wrapped as
    /// logging → validation → handler. Requires logging to be registered by the host.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(DependencyInjection).Assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IValidator<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationCommandHandlerDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(ValidationQueryHandlerDecorator<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandHandlerDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryHandlerDecorator<,>));

        return services;
    }
}
