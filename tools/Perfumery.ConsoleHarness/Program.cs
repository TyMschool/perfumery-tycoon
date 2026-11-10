using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Perfumery.Application;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Formulas.AddFormulaLine;
using Perfumery.Application.Formulas.CreateFormula;
using Perfumery.Application.Formulas.EvaluateFormula;
using Perfumery.Application.Materials.GetMaterials;
using Perfumery.Domain.Formulas;
using Perfumery.Infrastructure;
using Perfumery.SharedKernel;

// Composes a classic eau de Cologne concentrate through the application layer
// and prints how it smells on a blotter at each checkpoint.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

await using var provider = new ServiceCollection()
    .AddLogging()
    .AddApplication()
    .AddInfrastructure()
    .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

await using var scope = provider.CreateAsyncScope();
var services = scope.ServiceProvider;

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

var materials = Unwrap(await services
    .GetRequiredService<IQueryHandler<GetMaterialsQuery, Result<IReadOnlyList<MaterialResponse>>>>()
    .Handle(new GetMaterialsQuery(), CancellationToken.None))
    .ToDictionary(material => material.Id);

var formulaId = Unwrap(await services
    .GetRequiredService<ICommandHandler<CreateFormulaCommand, Result<FormulaId>>>()
    .Handle(new CreateFormulaCommand("Eau de Cologne"), CancellationToken.None));

var addLine = services.GetRequiredService<ICommandHandler<AddFormulaLineCommand, Result>>();
foreach (var (materialId, parts) in cologne)
{
    Check(await addLine.Handle(new AddFormulaLineCommand(formulaId.Value, materialId, parts), CancellationToken.None));
}

var profile = Unwrap(await services
    .GetRequiredService<IQueryHandler<EvaluateFormulaQuery, Result<ScentProfileResponse>>>()
    .Handle(new EvaluateFormulaQuery(formulaId.Value), CancellationToken.None));

Console.WriteLine($"{profile.FormulaName} (revision {profile.Revision})");
Console.WriteLine();
Console.WriteLine($"  {"Material",-30} {"Parts",6}  {"Stock",-6} {"Tier",-5}");
foreach (var (materialId, parts) in cologne)
{
    var material = materials[materialId];
    var stock = material.StockDilutionPercent == 100m ? "neat" : $"{material.StockDilutionPercent:0.#}%";
    Console.WriteLine($"  {material.Name,-30} {parts,6:0.##}  {stock,-6} {material.NoteTier,-5}");
}

Console.WriteLine();
Console.WriteLine($"  Cost per kg of concentrate: {profile.CostPerKgOfConcentrate:0.00} francs");
Console.WriteLine($"  Carrier from stock solutions: {profile.SolventFraction:P1}");

var startIntensity = profile.Checkpoints[0].TotalIntensity;
foreach (var checkpoint in profile.Checkpoints)
{
    Console.WriteLine();
    Console.WriteLine(
        $"{FormatElapsed(checkpoint.ElapsedMinutes),-7} intensity {checkpoint.TotalIntensity:0.00} " +
        $"({checkpoint.TotalIntensity / startIntensity:P0} of start), dominant: {checkpoint.DominantFacet ?? "nothing left"}");

    var facets = checkpoint.Facets
        .Where(facet => facet.Value >= 0.01)
        .OrderByDescending(facet => facet.Value)
        .Select(facet => $"{facet.Key} {facet.Value:P0}");
    Console.WriteLine($"  facets: {string.Join(", ", facets)}");

    var contributors = checkpoint.TopContributors.Select(c => $"{c.MaterialName} {c.Share:P0}");
    Console.WriteLine($"  top:    {string.Join(", ", contributors)}");
}

return 0;

static string FormatElapsed(double minutes) => minutes switch
{
    < 60 => $"{minutes:0} min",
    _ => $"{minutes / 60:0.#} h",
};

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
