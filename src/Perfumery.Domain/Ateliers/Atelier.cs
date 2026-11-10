using Perfumery.Domain.Common;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Ateliers;

public readonly record struct AtelierId(Guid Value)
{
    public static AtelierId New() => new(Guid.NewGuid());
}

public enum LedgerCategory
{
    StartingCapital,
    SuppliesPurchased,
}

/// <param name="At">When cash moved.</param>
/// <param name="Amount">Signed: positive in, negative out. Whole centimes.</param>
/// <param name="Category">Why.</param>
/// <param name="Reference">What caused it, e.g. the item bought.</param>
public sealed record LedgerEntry(DateTimeOffset At, decimal Amount, LedgerCategory Category, string Reference);

/// <summary>
/// The player's house: its cash, the ledger of every franc in and out, and the latest
/// moment the game was seen running. Cash always equals the ledger total.
/// </summary>
public sealed class Atelier : AggregateRoot<AtelierId>
{
    /// <summary>Older entries are folded into <see cref="CarriedForward"/>.</summary>
    public const int MaxLedgerEntries = 500;

    private readonly List<LedgerEntry> _ledger = [];

    private Atelier(AtelierId id, DateTimeOffset lastSeen)
        : base(id)
    {
        Cash = Money.Zero;
        CarriedForward = Money.Zero;
        LastSeen = lastSeen;
    }

    public Money Cash { get; private set; }

    /// <summary>The balance of all entries no longer listed individually.</summary>
    public Money CarriedForward { get; private set; }

    /// <summary>The most recent entries, oldest first.</summary>
    public IReadOnlyList<LedgerEntry> Ledger => _ledger.AsReadOnly();

    public DateTimeOffset LastSeen { get; private set; }

    public static Atelier Open(Money startingCapital, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(startingCapital);

        var atelier = new Atelier(AtelierId.New(), now);
        atelier.Record(now, startingCapital.ToCentimes().Amount, LedgerCategory.StartingCapital, "Starting capital");
        return atelier;
    }

    /// <summary>
    /// Notes the time and returns the time to use. The clock never runs backwards: if
    /// <paramref name="now"/> is earlier than the latest time seen, that latest time is used.
    /// </summary>
    public DateTimeOffset Observe(DateTimeOffset now)
    {
        LastSeen = EffectiveNow(now);
        return LastSeen;
    }

    /// <summary>The time a query should use, without recording anything.</summary>
    public DateTimeOffset EffectiveNow(DateTimeOffset now) => now > LastSeen ? now : LastSeen;

    public Result Spend(Money amount, LedgerCategory category, string reference, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(amount);

        var centimes = amount.ToCentimes();
        if (centimes.Amount > Cash.Amount)
        {
            return AtelierErrors.InsufficientFunds(centimes, Cash);
        }

        Record(now, -centimes.Amount, category, reference);
        return Result.Success();
    }

    public Result Receive(Money amount, LedgerCategory category, string reference, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(amount);

        Record(now, amount.ToCentimes().Amount, category, reference);
        return Result.Success();
    }

    /// <summary>Raises the event for a purchase already paid through <see cref="Spend"/>.</summary>
    public void NoteSuppliesPurchased(string item, decimal quantity, decimal amount) =>
        Raise(new SuppliesPurchasedDomainEvent(Id, item, quantity, amount));

    public AtelierSnapshot ToSnapshot() =>
        new(Id.Value, Cash.Amount, LastSeen, CarriedForward.Amount, [.. _ledger]);

    /// <summary>Rebuilds the atelier from saved state, re-checking every invariant.</summary>
    public static Result<Atelier> Restore(AtelierSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var cash = Money.Create(snapshot.Cash);
        var carried = Money.Create(snapshot.CarriedForward);
        if (Result.FirstFailure(cash, carried) is { } failure)
        {
            return failure;
        }

        if (snapshot.Ledger.Count > MaxLedgerEntries
            || snapshot.Ledger.Any(entry => !Enum.IsDefined(entry.Category))
            || carried.Value.Amount + snapshot.Ledger.Sum(entry => entry.Amount) != cash.Value.Amount)
        {
            return AtelierErrors.LedgerMismatch;
        }

        var atelier = new Atelier(new AtelierId(snapshot.Id), snapshot.LastSeen)
        {
            Cash = cash.Value,
            CarriedForward = carried.Value,
        };
        atelier._ledger.AddRange(snapshot.Ledger);
        return atelier;
    }

    private void Record(DateTimeOffset at, decimal amount, LedgerCategory category, string reference)
    {
        _ledger.Add(new LedgerEntry(at, amount, category, reference));
        Cash = Money.Create(Cash.Amount + amount).Value;

        while (_ledger.Count > MaxLedgerEntries)
        {
            CarriedForward = Money.Create(CarriedForward.Amount + _ledger[0].Amount).Value;
            _ledger.RemoveAt(0);
        }
    }
}

public sealed record AtelierSnapshot(
    Guid Id,
    decimal Cash,
    DateTimeOffset LastSeen,
    decimal CarriedForward,
    IReadOnlyList<LedgerEntry> Ledger);

public static class AtelierErrors
{
    public static readonly Error InvalidEquipment =
        Error.Validation("Atelier.InvalidEquipment", "Every kind of equipment needs a count and a capacity above zero.");

    public static readonly Error InvalidFilterLoss =
        Error.Validation("Atelier.InvalidFilterLoss", "Filter loss must be at least 0 and below 1.");

    public static readonly Error InvalidTimeScale =
        Error.Validation("Atelier.InvalidTimeScale", "The time scale must be above 0.");

    public static readonly Error InvalidDuration =
        Error.Validation("Atelier.InvalidDuration", "A duration cannot be negative.");

    public static readonly Error LedgerMismatch =
        Error.Validation("Atelier.LedgerMismatch", "The saved cash does not equal the saved ledger total.");

    public static Error InsufficientFunds(Money needed, Money cash) =>
        Error.Conflict("Atelier.InsufficientFunds", $"That costs {needed} francs and the atelier has {cash}.");

    public static Error NoFreeEquipment(EquipmentKind kind) =>
        Error.Conflict("Atelier.NoFreeEquipment", $"Every {kind} is in use.");
}
