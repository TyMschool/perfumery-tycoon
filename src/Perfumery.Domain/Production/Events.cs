using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Production;

public sealed record ExtractionStartedDomainEvent(ExtractionRunId RunId, ExtractionRecipeId RecipeId, DateTimeOffset CompletesAt)
    : DomainEvent;

public sealed record ExtractionCollectedDomainEvent(ExtractionRunId RunId, MaterialId MaterialId, decimal Grams)
    : DomainEvent;

public sealed record ConcentrateCompoundedDomainEvent(ConcentrateBatchId BatchId, FormulaRevisionRef Formula, decimal Grams)
    : DomainEvent;

public sealed record ProductRunStartedDomainEvent(ProductRunId RunId, ProductTypeId ProductTypeId, DateTimeOffset CompletesAt)
    : DomainEvent;

public sealed record ProductRunBottledDomainEvent(ProductRunId RunId, ProductTypeId ProductTypeId, int Units)
    : DomainEvent;
