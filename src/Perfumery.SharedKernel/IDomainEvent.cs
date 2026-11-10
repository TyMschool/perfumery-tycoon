namespace Perfumery.SharedKernel;

/// <summary>
/// Something that happened in the domain. Events carry no timestamp of their own:
/// no domain code reads a clock, and events that need a time take it as data.
/// </summary>
public interface IDomainEvent;

public abstract record DomainEvent : IDomainEvent;
