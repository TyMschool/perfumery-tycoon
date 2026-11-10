using System.Diagnostics.CodeAnalysis;

namespace Perfumery.SharedKernel;

/// <summary>
/// Lets generic code (such as handler decorators) build a failed result of the
/// right closed type without reflection.
/// </summary>
public interface IFailureFactory<out TSelf>
{
    static abstract TSelf FromError(Error error);
}

public class Result : IFailureFactory<Result>
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None || !isSuccess && error == Error.None)
        {
            throw new ArgumentException("A result is either successful or carries an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>The error of the first failed result, or null if all succeeded.</summary>
    public static Error? FirstFailure(params ReadOnlySpan<Result> results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                return result.Error;
            }
        }

        return null;
    }

    static Result IFailureFactory<Result>.FromError(Error error) => Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<TValue> : Result, IFailureFactory<Result<TValue>>
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>The value of a successful result. Reading it on a failure is a bug.</summary>
    [NotNull]
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    static Result<TValue> IFailureFactory<Result<TValue>>.FromError(Error error) => Failure<TValue>(error);

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
