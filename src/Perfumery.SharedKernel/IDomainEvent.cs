namespace Perfumery.SharedKernel;

public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
