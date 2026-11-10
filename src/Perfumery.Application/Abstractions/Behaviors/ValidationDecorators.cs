using FluentValidation;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Abstractions.Behaviors;

/// <summary>Rejects malformed commands before they reach the handler. Business rules stay in the domain.</summary>
internal sealed class ValidationCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : Result, IFailureFactory<TResponse>
{
    public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var error = await RequestValidation.Validate(validators, command, cancellationToken);
        return error is null
            ? await inner.Handle(command, cancellationToken)
            : TResponse.FromError(error);
    }
}

/// <summary>Rejects malformed queries before they reach the handler.</summary>
internal sealed class ValidationQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    IEnumerable<IValidator<TQuery>> validators)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : Result, IFailureFactory<TResponse>
{
    public async Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken)
    {
        var error = await RequestValidation.Validate(validators, query, cancellationToken);
        return error is null
            ? await inner.Handle(query, cancellationToken)
            : TResponse.FromError(error);
    }
}
