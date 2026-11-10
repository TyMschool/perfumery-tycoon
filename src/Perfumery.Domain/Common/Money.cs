using Perfumery.SharedKernel;

namespace Perfumery.Domain.Common;

/// <summary>A non-negative amount of the single in-game currency (francs in era 1).</summary>
public sealed record Money
{
    public static readonly Money Zero = new(0m);

    private Money(decimal amount)
    {
        Amount = amount;
    }

    public decimal Amount { get; }

    public static Result<Money> Create(decimal amount) =>
        amount < 0m ? MoneyErrors.Negative : new Money(amount);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    /// <summary>Scales by a non-negative factor such as a mass fraction.</summary>
    public Money Times(decimal factor) =>
        factor < 0m
            ? throw new ArgumentOutOfRangeException(nameof(factor), "Money cannot be scaled by a negative factor.")
            : new Money(Amount * factor);

    public override string ToString() => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

public static class MoneyErrors
{
    public static readonly Error Negative = Error.Validation("Money.Negative", "An amount of money cannot be negative.");
}
