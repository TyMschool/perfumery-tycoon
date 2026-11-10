using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Perfumery.Application;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Ateliers.GetAtelier;
using Perfumery.Application.Formulas.AddFormulaLine;
using Perfumery.Application.Formulas.CreateFormula;
using Perfumery.Application.Formulas.EvaluateFormula;
using Perfumery.Application.Game.LoadGame;
using Perfumery.Application.Game.StartNewGame;
using Perfumery.Application.Production.BottleProductRun;
using Perfumery.Application.Production.CollectExtraction;
using Perfumery.Application.Production.CompoundConcentrate;
using Perfumery.Application.Production.QuoteProduction;
using Perfumery.Application.Production.StartExtraction;
using Perfumery.Application.Production.StartProductRun;
using Perfumery.Application.Stock.GetInventory;
using Perfumery.Application.Stock.GetShelf;
using Perfumery.Application.Stock.PurchaseSupplies;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Production;
using Perfumery.Infrastructure;
using Perfumery.SharedKernel;

// Plays one production cycle through the application layer on a fake clock: buy supplies,
// distil lavender, compound an eau de Cologne, bottle it, save, then reload the save in a
// fresh session and print what is there.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var saveDirectory = Directory.CreateTempSubdirectory("perfumery-harness-");
var savePath = Path.Combine(saveDirectory.FullName, "save.json");
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));

(string MaterialId, decimal Parts)[] cologne =
[
    ("bergamot-calabria", 40m),
    ("lemon-sicily", 20m),
    ("sweet-orange-sicily", 10m),
    ("petitgrain-bigarade", 10m),
    ("neroli-vallauris", 10m),
    ("lavender-haute-provence", 6m),
    ("rosemary-dalmatia", 4m),
];

try
{
    // ---- First session: compose, produce, bottle ------------------------------------
    await using (var provider = BuildProvider())
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var loaded = Unwrap(await Command<LoadGameCommand, Result<LoadGameOutcome>>(services).Handle(new(), default));
        Console.WriteLine($"Load: {loaded}. Starting a new game.");
        Check(await Command<StartNewGameCommand, Result>(services).Handle(new(), default));

        var formulaId = Unwrap(await Command<CreateFormulaCommand, Result<FormulaId>>(services)
            .Handle(new CreateFormulaCommand("Eau de Cologne"), default)).Value;
        foreach (var (materialId, parts) in cologne)
        {
            Check(await Command<AddFormulaLineCommand, Result>(services)
                .Handle(new AddFormulaLineCommand(formulaId, materialId, parts), default));
        }

        var profile = Unwrap(await Query<EvaluateFormulaQuery, Result<ScentProfileResponse>>(services)
            .Handle(new EvaluateFormulaQuery(formulaId), default));
        PrintProfile(profile);

        // Lavender is distilled in the atelier; everything else is bought.
        Section("Distilling lavender");
        await Buy(services, "supply:lavender-flowering-tops", 50m);
        var extraction = Unwrap(await Command<StartExtractionCommand, Result<ExtractionRunId>>(services)
            .Handle(new StartExtractionCommand("distil-lavender", 50_000m), default));
        var early = await Command<CollectExtractionCommand, Result>(services)
            .Handle(new CollectExtractionCommand(extraction.Value), default);
        Console.WriteLine($"  Collecting at once: {early.Error.Description}");
        Advance(TimeSpan.FromMinutes(20));
        Check(await Command<CollectExtractionCommand, Result>(services)
            .Handle(new CollectExtractionCommand(extraction.Value), default));
        Console.WriteLine("  Collected 500 g of lavender oil.");

        // Ask what 300 g of concentrate as cologne would take, then buy exactly what is short.
        Section("Quote: 300 g of concentrate as eau de Cologne at 3%");
        var quoteQuery = new QuoteProductionQuery(formulaId, 300m, "eau-de-cologne");
        var quote = Unwrap(await Query<QuoteProductionQuery, Result<ProductionQuoteResponse>>(services)
            .Handle(quoteQuery, default));
        PrintQuote(quote);

        foreach (var line in quote.Requirements.Where(line => line.Missing > 0m))
        {
            var quantity = line.Unit == "Each" ? line.Missing : Math.Ceiling(line.Missing * 1000m) / 1000m;
            await Buy(services, line.Item, quantity);
        }

        quote = Unwrap(await Query<QuoteProductionQuery, Result<ProductionQuoteResponse>>(services).Handle(quoteQuery, default));
        Console.WriteLine($"  After buying the shortages: can produce = {quote.CanProduce}, "
            + $"{quote.Product!.Units} flacons at {quote.Product.UnitCost:0.0000} francs each.");

        Section("Producing");
        var batch = Unwrap(await Command<CompoundConcentrateCommand, Result<ConcentrateBatchId>>(services)
            .Handle(new CompoundConcentrateCommand(formulaId, 300m), default));
        Console.WriteLine("  Compounded 300 g of concentrate; maturing 15 min.");
        Advance(TimeSpan.FromMinutes(15));

        var run = Unwrap(await Command<StartProductRunCommand, Result<ProductRunId>>(services)
            .Handle(new StartProductRunCommand(batch.Value, "eau-de-cologne", 300m), default));
        Console.WriteLine("  Diluted to 3%; macerating 30 min.");
        Advance(TimeSpan.FromMinutes(30));

        Check(await Command<BottleProductRunCommand, Result>(services).Handle(new BottleProductRunCommand(run.Value), default));
        Console.WriteLine("  Filtered and bottled.");
    }

    // ---- Second session: a night later, from the save file --------------------------
    Advance(TimeSpan.FromHours(10));
    await using (var provider = BuildProvider())
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var loaded = Unwrap(await Command<LoadGameCommand, Result<LoadGameOutcome>>(services).Handle(new(), default));
        Section($"Reloaded from {Path.GetFileName(savePath)} ({new FileInfo(savePath).Length} bytes): {loaded}");

        Section("Shelf");
        foreach (var lot in Unwrap(await Query<GetShelfQuery, Result<IReadOnlyList<ShelfLotResponse>>>(services).Handle(new(), default)))
        {
            Console.WriteLine($"  {lot.Units} × {lot.ProductName} \"{lot.FormulaName}\" (revision {lot.FormulaRevision}, "
                + $"{lot.ConcentrationPercent:0.#}%) at {lot.UnitCost:0.0000} francs each");
        }

        Section("Inventory");
        Console.WriteLine($"  {"Item",-32} {"Quantity",10} {"Unit",-9} {"Avg cost",10} {"Value",9}");
        foreach (var line in Unwrap(await Query<GetInventoryQuery, Result<IReadOnlyList<StockLineResponse>>>(services).Handle(new(), default)))
        {
            Console.WriteLine($"  {line.Name,-32} {line.Quantity,10:0.#####} {line.Unit,-9} {line.AverageUnitCost,10:0.0000} {line.TotalCost,9:0.00}");
        }

        var atelier = Unwrap(await Query<GetAtelierQuery, Result<AtelierResponse>>(services).Handle(new(), default));
        Section("Ledger");
        foreach (var entry in atelier.Ledger.Reverse())
        {
            Console.WriteLine($"  {entry.At:HH:mm}  {entry.Amount,9:0.00}  {entry.Reference}");
        }

        Console.WriteLine($"  {"Cash",5}  {atelier.Cash,9:0.00}");
    }

    return 0;
}
finally
{
    saveDirectory.Delete(recursive: true);
}

ServiceProvider BuildProvider() =>
    new ServiceCollection()
        .AddLogging()
        .AddSingleton<TimeProvider>(clock)
        .AddApplication()
        .AddInfrastructure(options =>
        {
            options.Path = savePath;
            options.MinWriteInterval = TimeSpan.Zero;
        })
        .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

void Advance(TimeSpan duration) => clock.Advance(duration);

static async Task Buy(IServiceProvider services, string item, decimal quantity)
{
    Check(await Command<PurchaseSuppliesCommand, Result>(services).Handle(new PurchaseSuppliesCommand(item, quantity), default));
    Console.WriteLine($"  Bought {quantity:0.#####} of {item}");
}

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
}

static void PrintProfile(ScentProfileResponse profile)
{
    Section($"{profile.FormulaName} (revision {profile.Revision})");
    Console.WriteLine($"  Cost per kg of concentrate at catalog prices: {profile.CostPerKgOfConcentrate:0.00} francs");
    Console.WriteLine($"  Carrier from stock solutions: {profile.SolventFraction:P1}");

    var start = profile.Checkpoints[0].TotalIntensity;
    foreach (var checkpoint in profile.Checkpoints)
    {
        var facets = checkpoint.Facets
            .Where(facet => facet.Value >= 0.1)
            .OrderByDescending(facet => facet.Value)
            .Select(facet => $"{facet.Key} {facet.Value:P0}");
        Console.WriteLine($"  {FormatElapsed(checkpoint.ElapsedMinutes),-7} {checkpoint.TotalIntensity / start,5:P0} of opening intensity; {string.Join(", ", facets)}");
    }
}

static void PrintQuote(ProductionQuoteResponse quote)
{
    Console.WriteLine($"  {"Item",-32} {"Required",10} {"On hand",10} {"Missing",10}");
    foreach (var line in quote.Requirements)
    {
        Console.WriteLine($"  {line.Name,-32} {line.Required,10:0.#####} {line.Available,10:0.#####} {line.Missing,10:0.#####}");
    }

    Console.WriteLine($"  Cost to buy what is missing: {quote.CostToBuyMissing:0.00} francs");
}

static string FormatElapsed(double minutes) => minutes < 60 ? $"{minutes:0} min" : $"{minutes / 60:0.#} h";

static ICommandHandler<TCommand, TResponse> Command<TCommand, TResponse>(IServiceProvider services)
    where TCommand : ICommand<TResponse>
    where TResponse : Result =>
    services.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

static IQueryHandler<TQuery, TResponse> Query<TQuery, TResponse>(IServiceProvider services)
    where TQuery : IQuery<TResponse>
    where TResponse : Result =>
    services.GetRequiredService<IQueryHandler<TQuery, TResponse>>();

static T Unwrap<T>(Result<T> result)
{
    Check(result);
    return result.Value;
}

static void Check(Result result)
{
    if (result.IsFailure)
    {
        throw new InvalidOperationException($"{result.Error.Code}: {result.Error.Description}");
    }
}
