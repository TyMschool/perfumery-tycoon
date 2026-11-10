# Perfumery Tycoon

A chill, historically grounded perfumery tycoon, built phase by phase in .NET 10 with
no UI yet.

- **Phase 1** ([design](docs/phase-1-design.md)): the era 1 materials catalog, the Formula
  aggregate and the scent evaluation model.
- **Phase 2** ([design](docs/phase-2-design.md)): production. The atelier buys supplies,
  extracts some of its own materials, compounds formulas into concentrate, dilutes,
  macerates and bottles, on a clock that keeps running while the game is closed, and
  saves to disk.

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

The harness plays one production cycle on a fake clock: it buys supplies, distils
lavender, compounds an eau de Cologne, bottles it, then reloads the save in a fresh
session and prints the shelf, the inventory and the ledger.

## Layout

```
src/
  Perfumery.SharedKernel/     Result, Error, Entity, AggregateRoot, IDomainEvent
  Perfumery.Domain/           Materials, Formulas, Evaluation, Ateliers, Stock, Production
  Perfumery.Application/      Command/query slices, handler interfaces, decorators
  Perfumery.Infrastructure/   JSON reference data, in-memory game session, save file, DI wiring
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

All under `src/Perfumery.Infrastructure/`:

| File | Holds |
| --- | --- |
| `Materials/materials.era1.json` | Facet weights, odor strength, half-life, price, stock dilution |
| `Evaluation/evaluation.json` | Power-law exponent, checkpoints |
| `Stock/supplies.era1.json` | Raw botanicals, spirit and flacons, with prices |
| `Production/extraction.era1.json` | Extraction recipes: yield, duration, equipment |
| `Production/products.era1.json` | Product types: concentration range, maceration, flacon |
| `Ateliers/atelier.era1.json` | Starting capital, equipment, filter loss, time scale |

Note tier thresholds (top below 2 h half-life, base from 12 h) are in
`src/Perfumery.Domain/Materials/Volatility.cs`.

Every number is a game-tuned first-pass value chosen for relative plausibility. None
has yet been checked against a reference such as Arctander or Guenther.

## Save file

The game saves to `save.json` under the user's local application data folder, in a
`PerfumeryTycoon` directory, after every successful command. Pass options to
`AddInfrastructure` to change the path, keep the save in memory, or set a time scale for
playtesting.
