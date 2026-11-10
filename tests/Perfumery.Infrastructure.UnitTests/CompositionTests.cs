using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Perfumery.Application;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Formulas.AddFormulaLine;
using Perfumery.Application.Formulas.AdjustLineParts;
using Perfumery.Application.Formulas.ChangeLineDilution;
using Perfumery.Application.Formulas.CreateFormula;
using Perfumery.Application.Formulas.EvaluateFormula;
using Perfumery.Application.Formulas.RemoveFormulaLine;
using Perfumery.Application.Materials.GetMaterials;
using Perfumery.Domain.Evaluation;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Infrastructure.UnitTests;

/// <summary>
/// The whole stack as the UI will use it: real catalog, real decorators, in-memory
/// persistence. Also runs the design's plausibility checks against the shipped numbers.
/// </summary>
public sealed class CompositionTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public CompositionTests()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public void Every_slice_resolves_wrapped_in_logging_then_validation()
    {
        object[] handlers =
        [
            Command<CreateFormulaCommand, Result<FormulaId>>(),
            Command<AddFormulaLineCommand, Result>(),
            Command<AdjustLinePartsCommand, Result>(),
            Command<ChangeLineDilutionCommand, Result>(),
            Command<RemoveFormulaLineCommand, Result>(),
            Query<EvaluateFormulaQuery, Result<ScentProfileResponse>>(),
            Query<GetMaterialsQuery, Result<IReadOnlyList<MaterialResponse>>>(),
        ];

        handlers.ShouldAllBe(handler => handler.GetType().Name.StartsWith("Logging"));
    }

    [Fact]
    public void Evaluation_settings_come_from_configuration()
    {
        var settings = _provider.GetRequiredService<EvaluationSettings>();

        settings.PerceptionExponent.ShouldBe(0.5);
        settings.Checkpoints.Select(checkpoint => checkpoint.TotalMinutes).ShouldBe([0d, 15d, 60d, 240d, 1440d]);
        settings.TopContributorCount.ShouldBe(3);
    }

    [Fact]
    public async Task Malformed_input_is_rejected_by_the_validation_decorator()
    {
        var result = await Command<AddFormulaLineCommand, Result>()
            .Handle(new AddFormulaLineCommand(Guid.Empty, "bergamot-calabria", 10m), Ct);

        result.Error.ShouldBeOfType<ValidationError>();

        var evaluation = await Query<EvaluateFormulaQuery, Result<ScentProfileResponse>>()
            .Handle(new EvaluateFormulaQuery(Guid.Empty), Ct);

        evaluation.Error.ShouldBeOfType<ValidationError>();
    }

    [Fact]
    public async Task The_catalog_query_returns_all_15_materials_and_filters_by_tier()
    {
        var query = Query<GetMaterialsQuery, Result<IReadOnlyList<MaterialResponse>>>();

        (await query.Handle(new GetMaterialsQuery(), Ct)).Value.Count.ShouldBe(15);

        var bases = (await query.Handle(new GetMaterialsQuery(NoteTier.Base), Ct)).Value;
        bases.Select(material => material.Id).ShouldBe(
        [
            "orris-butter-tuscany",
            "vetiver-reunion",
            "sandalwood-mysore",
            "benzoin-tincture-siam",
            "tonka-bean-tincture-venezuela",
        ]);
    }

    [Fact]
    public async Task A_formula_can_be_composed_edited_and_evaluated_end_to_end()
    {
        var id = await Create("Rose water");
        await Add(id, "bergamot-calabria", 10m);
        await Add(id, "rose-otto-kazanlak", 10m);
        await Add(id, "clove-bud-zanzibar", 1m);

        (await Command<AdjustLinePartsCommand, Result>()
            .Handle(new AdjustLinePartsCommand(id, "bergamot-calabria", 30m), Ct)).IsSuccess.ShouldBeTrue();
        (await Command<ChangeLineDilutionCommand, Result>()
            .Handle(new ChangeLineDilutionCommand(id, "rose-otto-kazanlak", 20m), Ct)).IsSuccess.ShouldBeTrue();
        (await Command<RemoveFormulaLineCommand, Result>()
            .Handle(new RemoveFormulaLineCommand(id, "clove-bud-zanzibar"), Ct)).IsSuccess.ShouldBeTrue();

        var profile = await Evaluate(id);

        // 30 parts bergamot neat + 10 parts rose otto at 20%: neat fractions 0.75 and 0.05.
        profile.Revision.ShouldBe(7);
        profile.SolventFraction.ShouldBe(0.2m);
        profile.CostPerKgOfConcentrate.ShouldBe(0.75m * 40m + 0.05m * 2400m);
        profile.Checkpoints.Count.ShouldBe(5);
    }

    [Fact]
    public async Task An_eau_de_cologne_opens_citrus_and_has_mostly_faded_by_four_hours()
    {
        var profile = await Evaluate(await ComposeCologne());

        At(profile, 0).DominantFacet.ShouldBe("Citrus");
        At(profile, 15).DominantFacet.ShouldBe("Citrus");
        At(profile, 240).TotalIntensity.ShouldBeLessThan(At(profile, 0).TotalIntensity * 0.5);
    }

    [Fact]
    public async Task Sandalwood_and_vetiver_make_the_drydown_woody()
    {
        var id = await ComposeCologne();
        await Add(id, "sandalwood-mysore", 10m);
        await Add(id, "vetiver-reunion", 5m);

        var profile = await Evaluate(id);

        At(profile, 0).DominantFacet.ShouldBe("Citrus");
        At(profile, 1440).DominantFacet.ShouldBe("Woody");
    }

    [Fact]
    public async Task Doubling_a_material_raises_its_share_less_than_twofold()
    {
        async Task<double> ShareOfRosemary(decimal parts)
        {
            var id = await Create("Trio");
            await Add(id, "bergamot-calabria", 10m);
            await Add(id, "lemon-sicily", 10m);
            await Add(id, "rosemary-dalmatia", parts);
            return At(await Evaluate(id), 0).TopContributors.Single(c => c.MaterialId == "rosemary-dalmatia").Share;
        }

        var single = await ShareOfRosemary(2m);
        var doubled = await ShareOfRosemary(4m);

        doubled.ShouldBeGreaterThan(single);
        doubled.ShouldBeLessThan(single * 2d);
    }

    [Fact]
    public async Task The_same_formula_always_gives_the_same_profile()
    {
        var id = await ComposeCologne();

        var first = await Evaluate(id);
        var second = await Evaluate(id);

        second.CostPerKgOfConcentrate.ShouldBe(first.CostPerKgOfConcentrate);
        for (var i = 0; i < first.Checkpoints.Count; i++)
        {
            second.Checkpoints[i].TotalIntensity.ShouldBe(first.Checkpoints[i].TotalIntensity);
            second.Checkpoints[i].DominantFacet.ShouldBe(first.Checkpoints[i].DominantFacet);
            second.Checkpoints[i].Facets.ShouldBe(first.Checkpoints[i].Facets);
            second.Checkpoints[i].TopContributors.ShouldBe(first.Checkpoints[i].TopContributors);
        }
    }

    private async Task<Guid> ComposeCologne()
    {
        var id = await Create("Eau de Cologne");
        await Add(id, "bergamot-calabria", 40m);
        await Add(id, "lemon-sicily", 20m);
        await Add(id, "neroli-vallauris", 10m);
        await Add(id, "petitgrain-bigarade", 10m);
        await Add(id, "rosemary-dalmatia", 5m);
        return id;
    }

    private async Task<Guid> Create(string name)
    {
        var result = await Command<CreateFormulaCommand, Result<FormulaId>>().Handle(new CreateFormulaCommand(name), Ct);
        return result.Value.Value;
    }

    private async Task Add(Guid formulaId, string materialId, decimal parts)
    {
        var result = await Command<AddFormulaLineCommand, Result>()
            .Handle(new AddFormulaLineCommand(formulaId, materialId, parts), Ct);
        result.IsSuccess.ShouldBeTrue(result.Error.Description);
    }

    private async Task<ScentProfileResponse> Evaluate(Guid formulaId)
    {
        var result = await Query<EvaluateFormulaQuery, Result<ScentProfileResponse>>()
            .Handle(new EvaluateFormulaQuery(formulaId), Ct);
        return result.Value;
    }

    private static ScentCheckpointResponse At(ScentProfileResponse profile, double minutes) =>
        profile.Checkpoints.Single(checkpoint => checkpoint.ElapsedMinutes == minutes);

    private ICommandHandler<TCommand, TResponse> Command<TCommand, TResponse>()
        where TCommand : ICommand<TResponse>
        where TResponse : Result =>
        _scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

    private IQueryHandler<TQuery, TResponse> Query<TQuery, TResponse>()
        where TQuery : IQuery<TResponse>
        where TResponse : Result =>
        _scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();
}
