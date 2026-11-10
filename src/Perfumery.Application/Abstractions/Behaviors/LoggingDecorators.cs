using Microsoft.Extensions.Logging;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Abstractions.Behaviors;

internal static partial class RequestLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {RequestName}")]
    public static partial void Handling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {RequestName}")]
    public static partial void Succeeded(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} failed with {ErrorCode}: {ErrorDescription}")]
    public static partial void Failed(ILogger logger, string requestName, string errorCode, string errorDescription);

    public static async Task<TResponse> Around<TResponse>(ILogger logger, string requestName, Func<Task<TResponse>> handle)
        where TResponse : Result
    {
        Handling(logger, requestName);
        var result = await handle();
        if (result.IsSuccess)
        {
            Succeeded(logger, requestName);
        }
        else
        {
            Failed(logger, requestName, result.Error.Code, result.Error.Description);
        }

        return result;
    }
}

internal sealed class LoggingCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<LoggingCommandHandlerDecorator<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : Result
{
    public Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken) =>
        RequestLog.Around(logger, typeof(TCommand).Name, () => inner.Handle(command, cancellationToken));
}

internal sealed class LoggingQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    ILogger<LoggingQueryHandlerDecorator<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : Result
{
    public Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken) =>
        RequestLog.Around(logger, typeof(TQuery).Name, () => inner.Handle(query, cancellationToken));
}
