# Perfumery Tycoon

A chill, historically grounded perfumery tycoon. This repository currently holds
**Phase 1**: the domain core in .NET 10 — the era 1 materials catalog, the Formula
aggregate and the scent evaluation model — with no UI.

Design: [docs/phase-1-design.md](docs/phase-1-design.md)

## Run it

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project tools/Perfumery.ConsoleHarness
```

The harness composes an eau de Cologne through the application layer and prints its
profile on a blotter at 0 min, 15 min, 1 h, 4 h and 24 h.

## Layout

```
src/
  Perfumery.SharedKernel/     Result, Error, Entity, AggregateRoot, IDomainEvent
  Perfumery.Domain/           Materials, Formulas, Evaluation (depends on SharedKernel only)
  Perfumery.Application/      Command/query slices, handler interfaces, decorators
  Perfumery.Infrastructure/   JSON materials catalog, in-memory repository, DI wiring
tests/
  Perfumery.Domain.UnitTests/
  Perfumery.Application.UnitTests/
  Perfumery.Infrastructure.UnitTests/
  Perfumery.ArchitectureTests/
tools/
  Perfumery.ConsoleHarness/
```

Dependencies point inward only and are enforced by the architecture tests.

## Tuning

- Material numbers (facet weights, odor strength, half-life, cost, stock dilution):
  `src/Perfumery.Infrastructure/Materials/materials.era1.json`
- Evaluation parameters (power-law exponent, checkpoints):
  `src/Perfumery.Infrastructure/Evaluation/evaluation.json`
- Note tier thresholds (top below 2 h half-life, base from 12 h):
  `src/Perfumery.Domain/Materials/Volatility.cs`

The material numbers are game-tuned first-pass values chosen for relative
plausibility. They have not yet been checked against a reference such as Arctander.
