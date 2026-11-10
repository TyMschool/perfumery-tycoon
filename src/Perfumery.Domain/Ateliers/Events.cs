using Perfumery.SharedKernel;

namespace Perfumery.Domain.Ateliers;

public sealed record SuppliesPurchasedDomainEvent(AtelierId AtelierId, string Item, decimal Quantity, decimal Amount)
    : DomainEvent;
