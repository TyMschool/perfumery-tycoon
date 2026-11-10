namespace Perfumery.SharedKernel;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
}

/// <summary>An expected, typed failure. Exceptions are reserved for bugs.</summary>
public record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
}

/// <summary>Several input-shape problems reported together.</summary>
public sealed record ValidationError(IReadOnlyList<Error> Errors)
    : Error("Validation.General", "One or more validation errors occurred.", ErrorType.Validation);
