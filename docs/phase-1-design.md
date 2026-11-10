# Perfumery Tycoon — Phase 1 Design

Nov 9, 2026 · @ty

## Overview

Phase 1 builds the game's domain core in .NET: the era 1 materials catalog, the Formula aggregate and the scent evaluation model, fully unit-tested, with no UI.

The game is a chill, historically grounded perfumery tycoon. You start as a small atelier making distillations and colognes, and rival houses create landmark products that open market moments you can recreate or reinterpret. Discovery events let you make history yourself. Eras advance on milestones, production runs idle, and formulation is the active play through a slider-based composer. Accuracy to real perfumery practice is a core requirement.

Phase 1 proves the foundation everything else depends on: if formulas, dilutions and evaluation are right, briefs, production and progression can be layered on top.

## Scope

**In Phase 1:**

- Solution skeleton with Domain, Application and Infrastructure projects, plus test projects
- Shared kernel: `Result`, `Error`, `Entity`, `AggregateRoot`, domain event base
- Materials catalog: 15 era 1 natural materials as curated reference data
- Formula aggregate: lines in parts, dilutions, normalization, invariants, cost per kg of concentrate
- Evaluation model: evaporation curve and facet profile at fixed checkpoints
- Application slices for composing a formula and evaluating it
- In-memory persistence behind repository interfaces
- Unit tests and architecture tests

**Not in Phase 1:**

- UI of any kind
- Production (extraction, aging, inventory, timers)
- Briefs, market, rivals, scoring against targets
- Eras beyond era 1, progression, skill tree
- IFRA compliance (IFRA forms in era 4; the rule interface can be stubbed)
- Finished product types (EdC, EdT and so on) and alcohol dilution
- Saving to disk or a database

## Solution structure

One .NET 10 (LTS) solution, four source projects, dependencies pointing inward only.

```
Perfumery.sln
  src/
    Perfumery.SharedKernel/      Result, Error, Entity, AggregateRoot, IDomainEvent
    Perfumery.Domain/
      Materials/                 Material, MaterialId, Facets, Volatility, ExtractionMethod
      Formulas/                  Formula (aggregate), FormulaLine, Parts, Dilution, FormulaErrors, events
      Evaluation/                ScentEvaluator, EvaporationModel, ScentProfile
    Perfumery.Application/
      Abstractions/              ICommand, IQuery, handler interfaces, IMaterialCatalog, IFormulaRepository
      Formulas/                  one folder per slice: CreateFormula, AddLine, AdjustParts, RemoveLine, EvaluateFormula
    Perfumery.Infrastructure/
      Materials/                 JSON-backed catalog (embedded resource)
      Persistence/               InMemoryFormulaRepository
      DependencyInjection.cs
  tests/
    Perfumery.Domain.UnitTests/
    Perfumery.Application.UnitTests/
    Perfumery.ArchitectureTests/
```

**Dependency rules** (enforced by architecture tests):

- Domain depends only on SharedKernel. No EF, no JSON, no DI references.
- Application depends on Domain. It defines the interfaces Infrastructure implements.
- Infrastructure depends on Application. Nothing depends on Infrastructure except the future UI host's composition root.
- The UI (Phase 4) depends on Application only, apart from wiring up DI.

**Conventions:**

- Nullable reference types on, warnings as errors, central package management via `Directory.Packages.props`.
- Entities have private setters and factory methods; constructors aren't public.
- Expected failures return `Result`/`Result<T>` with a typed `Error`; exceptions are for bugs only.
- No MediatR (now commercially licensed). Lightweight `ICommandHandler<TCommand, TResponse>` and `IQueryHandler<TQuery, TResponse>` interfaces, registered with Scrutor, with decorators for validation and logging.

## Domain model

The Formula is the only aggregate in Phase 1; materials are immutable reference data that formulas point to by ID.

### Value objects

| Type | Represents | Rules |
| --- | --- | --- |
| `MaterialId` | Stable slug, e.g. `bergamot-calabria` | Non-empty, lowercase, hyphenated |
| `Parts` | A line's amount in the formula | 0.01 to 1000; log-scale slider maps onto this |
| `Dilution` | Share of neat material in its stock solution | Above 0 and up to 100%; 100% = neat |
| `Money` | Game currency amount | Non-negative, single currency |
| `Facets` | Odor character: citrus, green, aromatic, floral, spicy, woody, earthy, balsamic, powdery, animalic | Each 0 to 1 |
| `Volatility` | Evaporation half-life in hours on a blotter | Above 0; derives `NoteTier` (top, heart, base) |

### Material (entity, reference data)

Id, name, botanical source, origin, `ExtractionMethod` (steam distillation, cold expression, enfleurage, tincture), facets, odor strength (relative impact multiplier), volatility, cost per kg neat, and the default stock dilution it's kept at in the lab.

### Formula (aggregate root)

State: `FormulaId`, name, revision number, and a list of `FormulaLine` (material ID, parts, dilution).

Behavior:

- `Formula.Create(name)` returns `Result<Formula>`.
- `AddLine(Material, Parts, Dilution?)`: takes the `Material` itself so a line can't reference a material that doesn't exist; dilution defaults to the material's stock dilution.
- `AdjustParts`, `ChangeDilution`, `RemoveLine`.
- Each successful change increments the revision.

Derived values:

- **Neat fraction** of a line = parts × dilution ÷ total parts. This is how much actual material ends up in the concentrate.
- **Solvent fraction** = 1 − sum of neat fractions, the carrier coming from diluted stock solutions.
- Percentages are always computed, never stored, so the slider never has to keep things summing to 100.

Invariants:

- Name is required, 80 characters max.
- No material appears twice; adjust the existing line instead.
- At most 40 lines.
- Parts and dilution stay within their value object ranges.

### Errors

`FormulaErrors` is a static class of typed errors: `NameRequired`, `MaterialAlreadyPresent`, `LineNotFound`, `TooManyLines`, `EmptyFormula` (for evaluation), plus range errors from the value objects.

### Domain events

`FormulaCreatedDomainEvent` and `FormulaRevisedDomainEvent` (formula ID, revision). In Phase 1 they're raised and collected on the aggregate but have no handlers; later phases subscribe (for example, to autosave or update a formula archive).

## Era 1 materials catalog

Fifteen naturals available to a mid-1800s atelier, enough to build an eau de Cologne, a lavender water and a simple rose or oriental base.

| Material | Origin | Extraction | Tier | Main facets | Lab stock |
| --- | --- | --- | --- | --- | --- |
| Bergamot oil | Calabria | Cold expression | Top | Citrus, aromatic | Neat |
| Lemon oil | Sicily | Cold expression | Top | Citrus | Neat |
| Sweet orange oil | Sicily | Cold expression | Top | Citrus | Neat |
| Neroli oil | Vallauris, France | Steam distillation | Top–heart | Floral, citrus, green | 10% |
| Petitgrain bigarade | Southern France | Steam distillation | Top | Green, citrus, aromatic | Neat |
| Rosemary oil | Dalmatia | Steam distillation | Top | Aromatic, green | Neat |
| Lavender oil | Haute-Provence | Steam distillation | Top–heart | Aromatic, floral | Neat |
| Rose otto | Kazanlak, Bulgaria | Steam distillation | Heart | Floral, spicy | 10% |
| Jasmine (enfleurage extract) | Grasse | Enfleurage | Heart | Floral, animalic | 10% |
| Orris butter | Tuscany | Steam distillation of aged rhizomes | Heart–base | Powdery, floral, earthy | 10% |
| Clove bud oil | Zanzibar | Steam distillation | Heart | Spicy | 10% |
| Vetiver oil | Réunion | Steam distillation | Base | Woody, earthy | Neat |
| Sandalwood oil | Mysore, India | Steam distillation | Base | Woody, balsamic | Neat |
| Benzoin tincture | Siam | Tincture in alcohol | Base | Balsamic, powdery | As tincture |
| Tonka bean tincture | Venezuela | Tincture in alcohol | Base | Balsamic, powdery | As tincture |

Numeric values (facet weights, odor strength, half-life, cost) are game-tuned and set during implementation, stored in `materials.era1.json`. They should be checked for relative plausibility against a standard reference such as Arctander's *Perfume and Flavor Materials of Natural Origin*: citrus fades in a couple of hours, sandalwood and vetiver last days, rose otto and orris cost far more per kg than citrus.

Tinctures are modeled as their own materials at 100% stock, with low odor strength, because the tincture itself is what the perfumer adds.

## Evaluation model

`ScentEvaluator` is a pure domain service: given a formula and its materials, it returns a `ScentProfile` describing the blend on a blotter at five checkpoints (0 min, 15 min, 1 h, 4 h, 24 h), plus cost per kg of concentrate.

For material *i* with neat fraction *n*, odor strength *s* and half-life *h*, the share still on the blotter at time *t*:

```latex
r_i(t) = 2^{-t / h_i}
```

Raw intensity, then perceived intensity using a power law (perceived odor intensity grows slower than concentration):

```latex
I_i(t) = n_i \, s_i \, r_i(t) \qquad P_i(t) = I_i(t)^{k}, \quad k \approx 0.5
```

Facet profile at each checkpoint, normalized so the shape sums to 1:

```latex
F(t) = \frac{\sum_i P_i(t)\, \mathbf{f}_i}{\left\lVert \sum_i P_i(t)\, \mathbf{f}_i \right\rVert_1}
```

Each checkpoint in the `ScentProfile` holds total perceived intensity, the normalized facet vector, and the three materials contributing most.

Cost per kg of concentrate = sum of neat fraction × material cost per kg neat.

**Known simplifications:** no masking, synergy or anosmia; facets add linearly. That's acceptable for a game as long as the results stay plausible. The exponent *k* and all material values are tuning parameters kept in configuration, not hard-coded.

## Application layer

Each use case is a vertical slice: a command or query record, its handler, and a validator, in one folder.

| Slice | Type | Input | Returns |
| --- | --- | --- | --- |
| `CreateFormula` | Command | Name | `Result<FormulaId>` |
| `AddFormulaLine` | Command | Formula ID, material ID, parts, optional dilution | `Result` |
| `AdjustLineParts` | Command | Formula ID, material ID, parts | `Result` |
| `ChangeLineDilution` | Command | Formula ID, material ID, dilution | `Result` |
| `RemoveFormulaLine` | Command | Formula ID, material ID | `Result` |
| `EvaluateFormula` | Query | Formula ID | `Result<ScentProfileResponse>` |
| `GetMaterials` | Query | Optional note tier filter | `Result<IReadOnlyList<MaterialResponse>>` |

Handler pattern for commands:

1. Load the formula from `IFormulaRepository`; return `FormulaErrors.NotFound` if missing.
2. Look up materials from `IMaterialCatalog`; return `MaterialErrors.NotFound` if missing.
3. Call the aggregate method and return its `Result` on failure.
4. Save through the repository.

Queries return response DTOs, never domain objects, so the UI can't reach into the domain.

Cross-cutting decorators: a validation decorator (FluentValidation) for input shape, and a logging decorator. Business rules stay in the domain; validators only check that input is well formed.

Interfaces defined here and implemented in Infrastructure: `IMaterialCatalog` (JSON-backed) and `IFormulaRepository` (in-memory for Phase 1).

## Testing and definition of done

The domain is tested without mocks; the application layer is tested with in-memory fakes; architecture rules are tested so the layering can't erode.

**Tools:** xUnit, Shouldly, NSubstitute where a fake is awkward, NetArchTest.Rules.

**Domain unit tests:**

- Every value object rejects out-of-range input.
- Formula invariants: duplicate material, too many lines, unknown line, empty name.
- Neat fractions: a known formula (for example, 10 parts bergamot neat + 10 parts rose otto at 10%) gives the expected fractions and solvent share.
- Revision increments and the right domain event is raised on each change.

**Evaluation tests (plausibility, not exact numbers):**

- An eau de Cologne built from bergamot, lemon, neroli, petitgrain and rosemary is citrus-dominant at 0 and 15 min and has lost most of its intensity by 4 h.
- Adding sandalwood and vetiver makes woody the dominant facet at 24 h.
- Doubling a material's parts raises its share less than twofold (the power law).
- The same input always gives the same profile.

**Architecture tests:** Domain has no reference to Application or Infrastructure; Application has none to Infrastructure; handlers are sealed; domain entities have no public setters.

**Definition of done:**

- [ ] Solution builds with warnings as errors
- [ ] All 15 era 1 materials load from JSON
- [ ] Formula aggregate and value objects complete with tests
- [ ] `ScentEvaluator` passes the plausibility tests
- [ ] All seven application slices implemented and tested
- [ ] Architecture tests pass
- [ ] A small console harness composes an eau de Cologne and prints its profile at each checkpoint

## Open decisions

- [ ] **UI host (Phase 4).** Recommended: Blazor WebAssembly, so the C# domain runs in the browser with no server and offline progress works naturally. Alternative: React front end over an ASP.NET Core API.
- [ ] **Facet set.** Are the ten facets enough, or should fruity, sweet and fresh be separate from citrus and balsamic?
- [ ] **Era 1 currency.** Francs, for period accuracy, or a neutral game currency?
- [ ] **Material numbers.** Who tunes the facet weights, strengths and half-lives, and against which references?
