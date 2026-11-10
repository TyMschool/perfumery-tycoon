using Perfumery.SharedKernel;

namespace Perfumery.Application.Abstractions.Messaging;

/// <summary>A request that changes state. <typeparamref name="TResponse"/> is a <see cref="Result"/> or <see cref="Result{TValue}"/>.</summary>
public interface ICommand<TResponse>
    where TResponse : Result;

/// <summary>A command that returns no value beyond success or failure.</summary>
public interface ICommand : ICommand<Result>;

/// <summary>A request that reads state without changing it.</summary>
public interface IQuery<TResponse>
    where TResponse : Result;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : Result
{
    Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : Result
{
    Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
}
