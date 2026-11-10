using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Perfumery.Application.Abstractions.Behaviors;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Formulas.AddFormulaLine;
using Perfumery.Application.Formulas.AdjustLineParts;
using Perfumery.Application.Formulas.ChangeLineDilution;
using Perfumery.Application.Formulas.CreateFormula;
using Perfumery.Application.Formulas.EvaluateFormula;
using Perfumery.Application.Formulas.RemoveFormulaLine;
using Perfumery.Application.Materials.GetMaterials;
using Perfumery.Domain.Formulas;
using Perfumery.Domain.Materials;
using Perfumery.SharedKernel;

namespace Perfumery.Application.UnitTests.Behaviors;

public class DecoratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Validation_stops_a_malformed_command_before_the_handler()
    {
        var inner = Substitute.For<ICommandHandler<AddFormulaLineCommand, Result>>();
        var decorator = new ValidationCommandHandlerDecorator<AddFormulaLineCommand, Result>(
            inner, [new AddFormulaLineCommandValidator()]);

        var result = await decorator.Handle(new AddFormulaLineCommand(Guid.Empty, "", 10m), Ct);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Type.ShouldBe(ErrorType.Validation);
        error.Errors.Select(e => e.Code).ShouldBe(["Validation.FormulaId", "Validation.MaterialId"]);
        await inner.DidNotReceiveWithAnyArgs().Handle(default!, Ct);
    }

    [Fact]
    public async Task Validation_passes_a_well_formed_command_through()
    {
        var command = new AddFormulaLineCommand(Guid.NewGuid(), "bergamot-calabria", 10m);
        var inner = Substitute.For<ICommandHandler<AddFormulaLineCommand, Result>>();
        inner.Handle(command, Ct).Returns(Result.Success());
        var decorator = new ValidationCommandHandlerDecorator<AddFormulaLineCommand, Result>(
            inner, [new AddFormulaLineCommandValidator()]);

        var result = await decorator.Handle(command, Ct);

        result.IsSuccess.ShouldBeTrue();
        await inner.Received(1).Handle(command, Ct);
    }

    [Fact]
    public async Task Validation_builds_a_typed_failure_for_value_returning_requests()
    {
        var inner = Substitute.For<IQueryHandler<EvaluateFormulaQuery, Result<ScentProfileResponse>>>();
        var decorator = new ValidationQueryHandlerDecorator<EvaluateFormulaQuery, Result<ScentProfileResponse>>(
            inner, [new EvaluateFormulaQueryValidator()]);

        Result<ScentProfileResponse> result = await decorator.Handle(new EvaluateFormulaQuery(Guid.Empty), Ct);

        result.Error.ShouldBeOfType<ValidationError>();
        await inner.DidNotReceiveWithAnyArgs().Handle(default!, Ct);
    }

    [Fact]
    public async Task Validation_with_no_validators_passes_through()
    {
        var command = new CreateFormulaCommand("Chypre");
        var id = FormulaId.New();
        var inner = Substitute.For<ICommandHandler<CreateFormulaCommand, Result<FormulaId>>>();
        inner.Handle(command, Ct).Returns(Result.Success(id));
        var decorator = new ValidationCommandHandlerDecorator<CreateFormulaCommand, Result<FormulaId>>(inner, []);

        (await decorator.Handle(command, Ct)).Value.ShouldBe(id);
    }

    [Fact]
    public async Task Logging_returns_the_inner_result_unchanged()
    {
        var failure = Result.Failure(FormulaErrors.TooManyLines);
        var command = new RemoveFormulaLineCommand(Guid.NewGuid(), "vetiver");
        var inner = Substitute.For<ICommandHandler<RemoveFormulaLineCommand, Result>>();
        inner.Handle(command, Ct).Returns(failure);
        var decorator = new LoggingCommandHandlerDecorator<RemoveFormulaLineCommand, Result>(
            inner, NullLogger<LoggingCommandHandlerDecorator<RemoveFormulaLineCommand, Result>>.Instance);

        (await decorator.Handle(command, Ct)).ShouldBeSameAs(failure);
    }

    [Fact]
    public void Validators_check_input_shape_only()
    {
        Invalid(new CreateFormulaCommandValidator(), new CreateFormulaCommand(null!));
        Valid(new CreateFormulaCommandValidator(), new CreateFormulaCommand(""));

        Invalid(new AdjustLinePartsCommandValidator(), new AdjustLinePartsCommand(Guid.Empty, "vetiver", 1m));
        Invalid(new AdjustLinePartsCommandValidator(), new AdjustLinePartsCommand(Guid.NewGuid(), " ", 1m));
        Valid(new AdjustLinePartsCommandValidator(), new AdjustLinePartsCommand(Guid.NewGuid(), "vetiver", -1m));

        Invalid(new ChangeLineDilutionCommandValidator(), new ChangeLineDilutionCommand(Guid.Empty, "vetiver", 10m));
        Valid(new ChangeLineDilutionCommandValidator(), new ChangeLineDilutionCommand(Guid.NewGuid(), "vetiver", 500m));

        Invalid(new RemoveFormulaLineCommandValidator(), new RemoveFormulaLineCommand(Guid.NewGuid(), ""));
        Valid(new RemoveFormulaLineCommandValidator(), new RemoveFormulaLineCommand(Guid.NewGuid(), "vetiver"));

        Invalid(new GetMaterialsQueryValidator(), new GetMaterialsQuery((NoteTier)42));
        Valid(new GetMaterialsQueryValidator(), new GetMaterialsQuery());
        Valid(new GetMaterialsQueryValidator(), new GetMaterialsQuery(NoteTier.Base));
    }

    private static void Valid<T>(IValidator<T> validator, T request) =>
        validator.Validate(request).IsValid.ShouldBeTrue();

    private static void Invalid<T>(IValidator<T> validator, T request) =>
        validator.Validate(request).IsValid.ShouldBeFalse();
}
