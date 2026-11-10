using Perfumery.SharedKernel;

namespace Perfumery.Domain.Formulas;

public sealed record FormulaCreatedDomainEvent(FormulaId FormulaId, int Revision) : DomainEvent;

public sealed record FormulaRevisedDomainEvent(FormulaId FormulaId, int Revision) : DomainEvent;
