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

    /// <summary>Rounded to the centime, as it appears in the ledger and in responses.</summary>
    public Money ToCentimes() => new(Math.Round(Amount, 2, MidpointRounding.AwayFromZero));

    /// <summary>
    /// The part of this amount that goes with <paramref name="part"/> out of <paramref name="whole"/>.
    /// Held to 10 decimal places so that shares and remainders always add back up exactly.
    /// </summary>
    public Money Share(decimal part, decimal whole) =>
        part < 0m || whole <= 0m || part > whole
            ? throw new ArgumentOutOfRangeException(nameof(part), "A share is a part of a positive whole.")
            : new Money(Math.Round(Amount * part / whole, 10, MidpointRounding.ToZero));

    /// <summary>Subtracts, failing a bug check if the result would be negative.</summary>
    public Money Minus(Money other) =>
        other.Amount > Amount
            ? throw new InvalidOperationException("Money cannot go below zero.")
            : new Money(Amount - other.Amount);

    public override string ToString() => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

public static class MoneyErrors
{
    public static readonly Error Negative = Error.Validation("Money.Negative", "An amount of money cannot be negative.");
}
