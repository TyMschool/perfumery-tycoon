using FluentValidation;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Abstractions.Behaviors;

internal static class RequestValidation
{
    /// <summary>Runs every validator for the request; null means the input is well formed.</summary>
    public static async Task<Error?> Validate<TRequest>(
        IEnumerable<IValidator<TRequest>> validators,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        foreach (var validator in validators)
        {
            var outcome = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(outcome.Errors.Select(failure =>
                Error.Validation($"Validation.{failure.PropertyName}", failure.ErrorMessage)));
        }

        return errors.Count == 0 ? null : new ValidationError(errors);
    }
}
