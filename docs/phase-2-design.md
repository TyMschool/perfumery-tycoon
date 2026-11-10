# Perfumery Tycoon — Phase 2 Design

Nov 10, 2026 · @ty

## Overview

Phase 2 builds production: the atelier buys materials, compounds a formula into concentrate, dilutes it into a finished product, ages it and bottles it, all on a game clock that keeps running while the player is away. Still no UI.

Phase 1 answered "what does this formula smell like and what does it cost per kg?" Phase 2 answers "can I afford to make it, how long does it take, and what do I have on the shelf?" It adds the idle half of the game beside the active half.

Phase 1 named briefs, production and progression as the layers to come but did not assign them to phases. This document assumes Phase 2 is production, and that Phase 3 covers briefs, the market, rivals and era progression. Production comes first because the market needs something to sell, and because the clock, stock and money it introduces are what Phase 3 builds on.

Selling is out of scope. Phase 2 ends with bottled stock on the shelf and its unit cost known.

## Scope

**In Phase 2:**

- Game clock: every timed job stores when it completes, so progress while away needs no simulation
- Atelier: cash, a ledger of every franc in and out, and fixed era 1 equipment capacity
- Inventory: materials, raw botanicals, spirit and flacons, with weighted-average cost
- Purchasing supplies at fixed catalog prices
- In-house extraction: steam distillation and tinctures for six of the fifteen materials
- Concentrate batches: compounding a formula revision from stock
- Product runs: dilution into one of three era 1 product types, maceration, filtering and bottling
- Batch and unit costing
- Saving to and loading from disk, with a versioned save format
- Application slices for all of the above, plus a production quote (a dry run)
- Unit tests, architecture tests and an extended console harness

**Not in Phase 2:**

- UI of any kind (Phase 4)
- Selling, prices, demand, briefs, rivals and scoring against targets (Phase 3)
- Eras beyond era 1, progression, skill tree, buying or upgrading equipment (Phase 3)
- Staff, wages and rent, including automation such as a foreman who collects and restarts jobs (planned as a Phase 3 unlock)
- Enfleurage in the atelier; jasmine extract is bought in
- Price changes over time; catalog prices are fixed
- Maturity affecting how a product smells or scores; it is recorded but not used yet
- IFRA compliance (era 4)
- Cloud saves and multiple save slots

## Changes to Phase 1 code

Five existing pieces change; nothing is removed, and the dependency rules stay as they are.

| Area | Today | Phase 2 |
| --- | --- | --- |
| Composition | `ScentEvaluator` and costing read a live `Formula` | `Formula.Compose()` returns an immutable `Composition` (material ID, neat fraction per line, solvent fraction). The evaluator and costing take a `Composition`, so a batch made from revision 7 still evaluates as revision 7 after the formula changes. |
| Carrier cost | Cost per kg of concentrate treats the carrier as free | The carrier is spirit and has a price. Costing takes the spirit cost per kg and adds solvent fraction × spirit cost. |
| Time | `DomainEvent` reads `DateTimeOffset.UtcNow` | No domain code reads a clock. Domain events no longer carry a timestamp of their own. Aggregate methods that depend on time take `now` as a parameter; handlers get it from `TimeProvider`. |
| Rehydration | Aggregates can only be built through `Create` | Each aggregate gains `ToSnapshot()` and `Restore(snapshot)`. Snapshots are plain domain records; `Restore` re-checks every invariant and returns a `Result`. |
| Formula deletion | Not possible | Still not possible. Batches refer to formulas by ID and revision, so formulas are kept. |

No new projects are added, only new folders: `Domain/Atelier`, `Domain/Stock`, `Domain/Production`, with matching slice folders in Application and `Infrastructure/Saves`.

The new reference data follows the materials pattern: curated JSON embedded in Infrastructure, loaded through an Application interface, invalid data throws at load.

| File | Holds |
| --- | --- |
| `supplies.era1.json` | Raw botanicals, spirit and flacons, with unit and price |
| `materials.era1.json` | Unchanged; the neat cost per kg is now also the purchase price |
| `extraction.era1.json` | Extraction recipes: inputs, yield, duration, equipment |
| `products.era1.json` | Product types: concentration range, maceration time, flacon |
| `atelier.era1.json` | Starting capital, equipment capacity, filter loss, time scale |

## Domain model

Phase 2 adds five aggregates beside `Formula`: the atelier, its inventory, extraction runs, concentrate batches and product runs. They refer to each other by ID only.

### Value objects

| Type | Represents | Rules |
| --- | --- | --- |
| `Mass` | An amount by weight, stored in grams | Non-negative, rounded to 0.01 g |
| `Quantity` | An amount of a stock item in that item's unit (kg or each) | Non-negative; whole numbers for items counted in units |
| `StockItemId` | Anything the inventory holds: a material, a raw botanical, spirit or a flacon | Kind plus slug, e.g. `material:lavender-haute-provence` |
| `Concentration` | Share of concentrate in a finished product, by mass | Above 0 and below 100%; must fall in the product type's range |
| `Yield` | Output mass per kg of raw botanical; for a tincture the output includes the spirit, less what the spent material retains | Above 0 |
| `FormulaRevisionRef` | Formula ID plus revision number | Revision at least 1 |
| `Composition` | A frozen formula: neat fraction per material, plus solvent fraction | Fractions sum to 1 with the solvent |
| `JobTiming` | Started at, completes at | Completes at is not before started at |

### Reference data (immutable, like `Material`)

- **`Supply`**: ID, name, kind (raw botanical, solvent, packaging), unit, price per unit.
- **`ExtractionRecipe`**: ID, output material, method, raw botanical, spirit per kg of raw (tinctures only), yield, duration, equipment kind.
- **`ProductType`**: ID, name, concentration range and default, maceration duration, flacon and its fill mass.

### Atelier (aggregate root, one per save)

State: cash, the ledger, equipment capacity, and the last moment the game was seen running.

- `Spend(amount, category, reference, now)` fails with `InsufficientFunds` if cash would go negative.
- `Receive(amount, category, reference, now)` exists for Phase 3; Phase 2 only uses it for the starting capital.
- Invariant: cash always equals the sum of the ledger entries.
- `Observe(now)` records the latest time seen and never moves backwards.

### Inventory (aggregate root, one per save)

State: one `StockLine` per item held, with quantity and weighted-average unit cost. It also holds the shelf: one lot per bottled product run, carrying the formula revision, composition, product type, concentration, units and unit cost.

- `Receive(item, quantity, unitCost)` adds stock and recomputes the average cost.
- `Reserve(requirements)` checks every line first and consumes nothing unless all are available. It returns what was taken and its cost.
- Invariant: no quantity is ever negative.
- A failed reservation lists every shortage, not just the first, so the UI can show the full shopping list.

### ExtractionRun (aggregate root)

State: recipe ID, raw mass charged, cost of inputs, timing, status (`Running`, `Collected`).

- `Start(recipe, rawMass, inputCost, now)` checks the whole charge, raw botanical plus any spirit, against the equipment's capacity.
- `Collect(now)` fails with `NotReady` before the completion time. It returns the output mass and its unit cost.
- A finished run stays in the equipment until collected, so the still is not free until then.

### ConcentrateBatch (aggregate root)

State: formula revision, its `Composition`, mass made, mass remaining, total cost, timing, status (`Maturing`, `Ready`, `Exhausted`).

- `Compound(formulaRef, composition, mass, cost, now)`.
- `Draw(mass, now)` fails before maturation ends or if more is asked for than remains. It returns the cost share of what was drawn.

### ProductRun (aggregate root)

State: concentrate batch ID, formula revision, product type, concentration, product mass, total cost, timing, status (`Macerating`, `Bottled`), and once bottled the unit count and unit cost.

- `Start(batch, productType, concentration, concentrateMass, costs, now)`.
- `Bottle(flaconsAvailable, now)` fails before maceration ends or without enough flacons.
- Bottling hands the units to the inventory as a shelf lot. The run is then finished; Phase 3 sells from the inventory, not from runs.

### Errors and events

Each aggregate has its own static errors class: `AtelierErrors`, `InventoryErrors`, `ExtractionErrors`, `ProductionErrors`. The ones the player will meet most are `InsufficientFunds`, `InsufficientStock` (with the shortages), `NoFreeEquipment`, `OverCapacity`, `NotReady` and `ConcentrationOutOfRange`.

New domain events: `SuppliesPurchased`, `ExtractionStarted`, `ExtractionCollected`, `ConcentrateCompounded`, `ProductRunStarted`, `ProductRunBottled`. As in Phase 1 they are raised and collected but have no subscribers yet. Phase 3 will use them for milestones.

## Production pipeline

Stock comes in by purchase or extraction, a formula turns it into concentrate, and a product run turns concentrate into bottles. Three of the steps take time.

*(Diagram in the original document: purchase and extraction feed the inventory; the inventory feeds a concentrate batch, then a product run, then the shelf. Extraction, maturing and maceration are the three timed steps.)*

1. **Purchase.** Buy materials, raw botanicals, spirit and flacons at catalog price. Delivery is instant.
2. **Extract** (optional, timed). Charge a still or a tincture jar with a raw botanical. When the run completes, collect the material into stock.
3. **Compound** (timed). Weigh out a formula revision into a concentrate batch of a chosen mass. Stock is consumed at once; the batch then matures before it can be used.
4. **Dilute and macerate** (timed). Draw concentrate from a ready batch, add spirit to reach the product's concentration, and leave it in a maceration vessel.
5. **Filter and bottle.** When maceration ends, filter (a small loss) and fill flacons. The units go on the shelf.

One concentrate batch can feed several product runs, so the same scent can be sold as a cologne and as an extrait.

### What compounding consumes

For a batch of mass *M* from a composition with neat fractions *n* and solvent fraction *v*, each material and the carrier spirit are drawn as:

```latex
m_i = M \, n_i \qquad m_{\text{carrier}} = M \, v
```

The inventory holds neat materials. Diluting to lab stock strength happens at the bench, which is why the carrier is taken from the spirit supply here. Tinctures are stocked as tinctures, as in Phase 1.

### What a product run consumes and yields

For concentrate mass *m* at concentration *c*, with filter loss *λ* and flacon fill mass *f*:

```latex
P = \frac{m}{c} \qquad m_{\text{spirit}} = P - m \qquad \text{units} = \left\lfloor \frac{P \, (1 - \lambda)}{f} \right\rfloor
```

Filter loss is 2% by default. What is left after the last full flacon is discarded; its cost is carried by the units that were filled.

### Era 1 product types

The modern EdT and EdP ladder does not exist yet. A mid-1800s house sells colognes, toilet waters and handkerchief extraits.

| Product | Concentration | Default | Maceration (game) | Flacon fill |
| --- | --- | --- | --- | --- |
| Eau de Cologne | 2–5% | 3% | 30 min | 200 g |
| Toilet water (lavender water, eau de toilette) | 5–10% | 7% | 1 h | 100 g |
| Extrait (handkerchief perfume) | 15–30% | 20% | 3 h | 30 g |

### Era 1 extraction recipes

Six materials can be made in the atelier. The other nine are bought in: the three citrus oils, rose otto, jasmine extract, orris butter, clove bud, vetiver and sandalwood.

| Material | Method | Raw botanical | Yield per kg raw | Duration (game) |
| --- | --- | --- | --- | --- |
| Lavender oil | Steam distillation | Lavender flowering tops | 10 g | 20 min |
| Rosemary oil | Steam distillation | Rosemary flowering tops | 10 g | 20 min |
| Petitgrain bigarade | Steam distillation | Bitter orange leaves and twigs | 3 g | 20 min |
| Neroli oil | Steam distillation | Bitter orange blossom | 1 g | 20 min |
| Benzoin tincture | Tincture | Benzoin resin, plus 5 kg spirit | 5 kg | 3 h |
| Tonka bean tincture | Tincture | Tonka beans, plus 8 kg spirit | 8 kg | 3 h |

The yields are approximate figures from memory, not yet checked against a reference such as Guenther's *The Essential Oils* or Arctander. All durations and prices are game-tuned. Raw botanical prices should be set so that making a material costs less than buying it, but not by much once the still's time is counted.

### Equipment in era 1

Capacity is fixed in Phase 2 and read from `atelier.era1.json`.

| Equipment | Count | Capacity each | Used by |
| --- | --- | --- | --- |
| Copper still | 1 | 100 kg of raw botanical | Steam distillation |
| Tincture jar | 2 | 10 kg of contents (raw plus spirit) | Tinctures |
| Maceration vessel | 3 | 20 kg of product | Product runs |

Concentrate batches are small and need no equipment slot.

### Game time against real practice

Durations are compressed but keep their real order: distilling is quick, a tincture is slow, an extrait rests longer than a cologne.

| Step | Real practice | Game |
| --- | --- | --- |
| Steam distillation | Hours | 20 min |
| Tincture | Weeks to months | 3 h |
| Concentrate maturing | Days to weeks | 15 min |
| Maceration | Weeks to months | 30 min to 3 h |

## Time and idle progress

Timed jobs run in real time and are never simulated: each stores the moment it completes, and it is done when the clock passes that moment. Closing the game for a night and reopening it needs no catch-up loop.

**The clock.** Handlers read the time from .NET's `TimeProvider`, registered in DI. Tests and the console harness use `FakeTimeProvider` and advance it by hand. The domain never reads a clock; `now` is always an argument.

**Deriving status.** A job's status is a function of its stored state and `now`. `ExtractionRun.IsComplete(now)` is true once `now` reaches the completion time. Nothing has to tick, and a query at any moment gives the right answer.

**Nothing completes on its own.** Finishing a job only makes the next action available. The player still collects the extraction or bottles the run. This keeps every state change tied to a command, and it means a job left overnight is simply waiting. The cost is that a night away yields one job per piece of equipment, which is why automation is planned as a Phase 3 unlock.

**Time scale.** `atelier.era1.json` holds a time scale multiplier, 1 by default. Durations in reference data are divided by it when a job starts, so a scale of 60 turns a 3-hour tincture into 3 minutes for playtesting. The completion time is fixed at the start; changing the scale later does not affect jobs already running.

**Clock going backwards.** The atelier stores the latest time it has seen. If the system clock reads earlier than that, handlers use the stored time instead, so progress never runs in reverse and no job gains time. Moving the clock forward to skip a wait is not prevented; this is a single-player game. A known side effect: if the clock jumps forward and is then corrected, running jobs look frozen until real time catches up with the stored time. It fixes itself and is accepted.

**Remaining time for the UI.** Queries return each job's completion time and the remaining duration at the moment of the query. The Phase 4 UI counts down locally from that and does not need to poll.

## Money and costing

Cash only leaves the atelier when supplies are bought. Everything after that is cost moving through stock: from supplies into a batch, from a batch into bottles.

**Ledger.** Every change in cash is one `LedgerEntry`: time, signed amount, category, and a reference to what caused it. Phase 2 has two categories, `StartingCapital` and `SuppliesPurchased`. Phase 3 adds sales. Starting capital is 5,000 francs, a placeholder to tune.

**Stock cost.** Each stock line carries a weighted-average unit cost. Receiving quantity *q* at unit cost *p* into a line holding *Q* at average *A* gives:

```latex
A' = \frac{Q \, A + q \, p}{Q + q}
```

Consuming stock takes it out at the current average and leaves the average unchanged.

**Cost of an extracted material.** The cost of the raw botanical and any spirit charged, divided by the mass collected. It enters stock at that unit cost and averages with any bought stock of the same material.

**Cost of a concentrate batch.** The sum of what was consumed, each at its average cost at that moment, including the carrier spirit. Drawing part of a batch takes the same share of its cost.

**Cost of a product run.** The concentrate's cost share, plus dilution spirit, plus the flacons filled. Unit cost is that total divided by the units bottled, so filter loss and the discarded remainder are paid for by the units that exist.

**Planned against actual cost.** Phase 1's cost per kg of concentrate uses catalog prices and answers "what would this formula cost?" A batch's cost uses what the stock actually cost and answers "what did this batch cost?" They differ whenever the atelier extracted its own materials. Both are kept.

**Rounding.** Mass is held to 0.01 g. A stock line holds its total cost and derives the average, so consuming stock never loses a fraction of a centime; unit costs are shown to 4 decimal places. Amounts are rounded to the centime only when they reach the ledger or a response DTO. Batch costs are carried unrounded so that splitting a batch across runs loses nothing.

## Application layer

Phase 2 adds eight commands and five queries, each a vertical slice in the Phase 1 pattern: record, handler, validator.

| Slice | Type | Input | Returns |
| --- | --- | --- | --- |
| `StartNewGame` | Command | None | `Result` |
| `PurchaseSupplies` | Command | Stock item ID, quantity | `Result` |
| `StartExtraction` | Command | Recipe ID, raw mass | `Result<ExtractionRunId>` |
| `CollectExtraction` | Command | Extraction run ID | `Result` |
| `CompoundConcentrate` | Command | Formula ID, mass | `Result<ConcentrateBatchId>` |
| `StartProductRun` | Command | Concentrate batch ID, product type ID, concentration, concentrate mass | `Result<ProductRunId>` |
| `BottleProductRun` | Command | Product run ID | `Result` |
| `LoadGame` | Command | None | `Result` |
| `GetAtelier` | Query | None | Cash, equipment in use and free, last ledger entries |
| `GetInventory` | Query | Optional item kind | Stock lines with quantity and average cost |
| `GetProduction` | Query | None | Extraction runs, concentrate batches, product runs, each with timing |
| `GetShelf` | Query | None | Shelf lots from the inventory, with units and unit cost |
| `QuoteProduction` | Query | Formula ID, mass, optional product type and concentration | Requirements, shortages, cost, units, durations |

`QuoteProduction` is a dry run of compounding and, if a product type is given, of the product run. It changes nothing and returns the full list of shortages with what they would cost to buy. The composer UI will call it as sliders move.

**Commands that touch several aggregates.** `StartProductRun` draws from a concentrate batch, reserves spirit, checks for a free vessel and creates the run. A domain service per workflow (`ExtractionService`, `CompoundingService`, `BottlingService`) does this in two passes: check everything, then change everything. A failure in the first pass leaves no aggregate modified. These services are pure: the handler loads the aggregates and reference data and passes them in, and a service never touches the session, a catalog or the clock.

**Saving.** The handler does not save aggregates one by one. A new innermost decorator, `SaveGameDecorator`, writes the whole session after every successful command. Order from the outside in: logging, validation, save, handler. Failed commands and queries write nothing.

**Saves and the composer.** Formula commands fire as sliders move, so saving after every command would write the file dozens of times per drag. The UI sends a formula command when a slider is released, not on every step, and `FileSaveStore` coalesces writes to at most one per second, flushing on shutdown.

**Clock.** A small `IGameClock` wraps `TimeProvider` and the backwards-clock rule, and handlers take `now` from it once per command.

New interfaces defined in Application and implemented in Infrastructure: `ISupplyCatalog`, `IExtractionRecipeCatalog`, `IProductTypeCatalog`, `IAtelierSettings`, `IGameSession` and `ISaveStore`. `IGameSession` replaces the per-aggregate repository idea for the new aggregates: it holds the atelier, the inventory and the production collections for the running game, and `IFormulaRepository` becomes a view over it.

## Persistence

The whole game is one versioned JSON document, written after every successful command and read once at startup.

**What is saved.** The atelier (cash, ledger, last time seen), the inventory, every formula, and every extraction run, concentrate batch and product run. Reference data is never saved; the save holds IDs only, so retuning `materials.era1.json` takes effect in existing games.

**How.** `IGameSession` produces a `GameSnapshot` from the aggregates' `ToSnapshot()` methods. Infrastructure maps that to save DTOs and serializes with `System.Text.Json`. Loading runs the other way and ends in each aggregate's `Restore`, which re-checks invariants. The domain still has no JSON reference.

**Where.** `ISaveStore` reads and writes one named blob. Phase 2 ships `FileSaveStore`, which writes to a temporary file and renames it over `save.json`, so a crash mid-write cannot corrupt the save. Phase 4 can add a browser-storage implementation without touching anything else.

**Versioning.** The document starts with `schemaVersion`, 1 in Phase 2. Loading an older version runs a chain of migrations, each a function from one version's JSON to the next. Loading a newer version than the build understands fails with `SaveErrors.NewerVersion` and leaves the file alone.

**Failures.** A missing save is not an error; `LoadGame` reports that there is none and the caller starts a new game. A save that is unreadable, fails an invariant or refers to an ID missing from reference data fails with a typed error and is never overwritten automatically. The file is copied to `save.corrupt.json` first if the player chooses to start over.

**Ledger growth.** The ledger keeps its most recent 500 entries and a carried-forward balance for everything older, so the save stays small and cash still equals the ledger total.

**Finished jobs.** Collected extraction runs, and exhausted concentrate batches that no product run still references, are dropped from the save once their costs have moved into stock. Bottled product runs are dropped too, since their units live on as shelf lots in the inventory. Without this, the save grows with every job ever run, the same problem the ledger cap solves.

```json
{
  "schemaVersion": 1,
  "savedAt": "2026-11-10T09:30:00Z",
  "atelier": { "cash": 4212.50, "lastSeen": "...", "ledger": { "carriedForward": 0, "entries": [] } },
  "inventory": [ { "item": "material:lavender-haute-provence", "quantity": 0.42, "averageCost": 31.2075 } ],
  "formulas": [],
  "extractionRuns": [],
  "concentrateBatches": [],
  "productRuns": []
}
```

## Testing and definition of done

The Phase 1 approach carries over: the domain is tested without mocks, the application layer with in-memory fakes and a fake clock, and architecture rules are tested so the layering holds.

**Tools:** as Phase 1, plus `Microsoft.Extensions.TimeProvider.Testing` for `FakeTimeProvider`.

**Domain unit tests:**

- Every new value object rejects out-of-range input.
- Inventory: weighted-average cost after several receipts; a reservation with one shortage consumes nothing and reports every shortage.
- Atelier: spending more than the cash fails; cash equals the ledger total after any sequence of entries, including after old entries are carried forward.
- Extraction: collecting early fails; output mass is raw mass × yield; unit cost is input cost ÷ output mass; a charge over capacity fails.
- Concentrate: a known formula at a known mass consumes the expected mass of each material and of carrier spirit; drawing before maturation fails; drawing takes a proportional cost share.
- Product run: a worked example (for example, 300 g of concentrate at 3% into 200 g flacons with 2% loss gives 10 kg of product and 49 units); a concentration outside the product's range fails; bottling without enough flacons fails.
- A batch keeps its composition when its formula is revised afterwards.

**Conservation tests** (hold for any valid input, checked over generated cases):

- Mass: what goes into a batch or run equals what comes out plus recorded loss.
- Cost: the cost of everything consumed equals the cost carried by batches, bottled units and remaining stock. No cost appears or vanishes.

**Time tests:**

- A job is incomplete one tick before its completion time and complete at it.
- Start a tincture, save, advance the clock three days, load: the run is ready to collect.
- With the clock set back an hour, no job gains or loses progress.
- A time scale of 60 shortens new jobs sixtyfold and leaves running jobs alone.

**Persistence tests:**

- Save then load gives a session equal to the original, with jobs at every stage present.
- A save from a newer schema version, a truncated file and a save naming an unknown material each fail with their typed error and leave the file untouched.
- A version 0 fixture migrates to version 1, proving the migration chain runs.
- An interrupted write leaves the previous save readable.

**Application tests:** each slice's success path and each error it can return; a failed multi-aggregate command leaves every aggregate unchanged and writes no save; `QuoteProduction` agrees with what the real commands then consume and cost.

**Architecture tests:** the Phase 1 rules, plus: no type in Domain or Application references `DateTime.Now`, `DateTimeOffset.UtcNow` or `TimeProvider.System`; snapshot types have no behavior.

**Definition of done:**

- [ ] Solution builds with warnings as errors and all Phase 1 tests still pass
- [ ] `Composition` refactor complete; evaluator and costing no longer take a live `Formula`
- [ ] Supplies, extraction recipes, product types and atelier settings load from JSON
- [ ] The five new aggregates and their value objects complete with tests
- [ ] Conservation tests pass for mass and cost
- [ ] All thirteen new application slices implemented and tested
- [ ] Save, load and migration tests pass
- [ ] Architecture tests pass, including the no-clock rule
- [ ] The console harness, on a fake clock, buys supplies, distils lavender, compounds an eau de Cologne, bottles it, saves, reloads, and prints the shelf, the inventory and the ledger

## Open decisions

- [ ] **Phase split.** This document assumes Phase 2 is production and Phase 3 is briefs, market, rivals and progression. The alternative is briefs and scoring first, which makes formulation a game sooner but leaves nothing to sell.
- [ ] **Real time or a game calendar.** Recommended: real-time durations, as designed here, since eras advance on milestones and not on dates. Alternative: a compressed calendar where a day passes every few minutes and maceration is counted in weeks, which is more evocative but needs a simulation step.
- [ ] **Mass or volume.** Everything is by weight, including flacon fills, which is how a perfumer works and avoids density tables. Flacons were sold by volume; the UI could show millilitres using one fixed density.
- [ ] **Spirit strength and water.** One solvent, "spirit", stands for rectified spirit of wine at a fixed strength. A cologne's lower alcohol strength and added water are not modeled. Is that enough for era 1?
- [ ] **Bottling early.** Recommended for now: not allowed. Alternative: allow it with a lower recorded maturity, which becomes a real choice once Phase 3 scores products.
- [ ] **Stock costing.** Weighted average, as designed, or first-in first-out lots? Lots matter only if prices move or materials age, and neither happens before Phase 3.
- [ ] **Enfleurage.** Jasmine is bought in. Making it in the atelier means fat, chassis, repeated charges of flowers and an alcohol wash: a production chain of its own, perhaps better as an era 1 unlock in Phase 3.
- [ ] **Labour and overheads.** Production costs materials only. Should the still's time, wages or rent be costed, or do those arrive with staff in a later phase?
- [ ] **Numbers.** Yields, durations, capacities, prices and the 5,000 franc starting capital are placeholders. Who tunes them, and against which references? This joins the same open question from Phase 1.

## Implementation notes

Phase 2 is implemented on the `phase-2-production` branch. It differs from the design above in six small ways.

- **Equipment lives in settings, not on the atelier.** Capacity is read from `atelier.era1.json` each time, so retuning it applies to existing saves. The atelier aggregate holds cash, the ledger and the last time seen.
- **No `IAtelierSettings`.** `AtelierSettings` is a domain object injected directly, the same way `EvaluationSettings` is.
- **The save document is the domain snapshots.** Infrastructure serializes the snapshot records as they are, under `schemaVersion` and `savedAt`, with no second set of save DTOs. Migrations still run on the raw JSON before it is read. The example JSON above shows the idea, not the exact field names.
- **`LoadGame` returns an outcome.** It returns `Loaded` or `NoSave` so the caller can tell the two apart without an error.
- **`StartNewGame` sets any unloaded save aside.** If a save file exists and was not loaded, it is copied to `save.corrupt.json` before the new game overwrites it, whether or not it was damaged.
- **The domain folder is `Ateliers`.** A namespace and a class both called `Atelier` would clash.

Cost shares are held to 10 decimal places, so a part and its remainder always add back to the whole exactly.
