using Perfumery.Domain.Common;
using Perfumery.SharedKernel;

namespace Perfumery.Domain.Production;

public static class ExtractionErrors
{
    public static readonly Error YieldOutOfRange =
        Error.Validation("Extraction.YieldOutOfRange", "A yield must be above 0.");

    public static readonly Error RecipeIdMalformed =
        Error.Validation("Extraction.RecipeIdMalformed", "A recipe ID must be lowercase words joined by hyphens.");

    public static readonly Error RecipeInvalid =
        Error.Validation("Extraction.RecipeInvalid", "The extraction recipe is not valid.");

    public static readonly Error ChargeTooSmall =
        Error.Validation("Extraction.ChargeTooSmall", "That charge is too small to yield anything.");

    public static readonly Error AlreadyCollected =
        Error.Conflict("Extraction.AlreadyCollected", "That extraction has already been collected.");

    public static Error RecipeNotFound(string id) =>
        Error.NotFound("Extraction.RecipeNotFound", $"No extraction recipe with ID '{id}' exists.");

    public static Error RunNotFound(Guid id) =>
        Error.NotFound("Extraction.RunNotFound", $"No extraction run with ID '{id}' exists.");
}

public static class ProductionErrors
{
    public static readonly Error ConcentrationInvalid =
        Error.Validation("Production.ConcentrationInvalid", "A concentration must be above 0% and below 100%.");

    public static readonly Error TimingInvalid =
        Error.Validation("Production.TimingInvalid", "A job cannot complete before it starts.");

    public static readonly Error ProductTypeIdMalformed =
        Error.Validation("Production.ProductTypeIdMalformed", "A product type ID must be lowercase words joined by hyphens.");

    public static readonly Error ProductTypeInvalid =
        Error.Validation("Production.ProductTypeInvalid", "The product type is not valid.");

    public static readonly Error BatchTooSmall =
        Error.Validation("Production.BatchTooSmall", "That batch is too small to weigh out every material.");

    public static readonly Error AlreadyBottled =
        Error.Conflict("Production.AlreadyBottled", "That product run has already been bottled.");

    public static readonly Error NothingToBottle =
        Error.Conflict("Production.NothingToBottle", "That run does not hold enough product to fill one flacon.");

    public static readonly Error CorruptState =
        Error.Validation("Production.CorruptState", "A saved production job is not in a valid state.");

    public static Error NotReady(DateTimeOffset completesAt) =>
        Error.Conflict("Production.NotReady", $"That is not ready until {completesAt:u}.");

    public static Error OverCapacity(Mass charge, Mass capacity) =>
        Error.Conflict("Production.OverCapacity", $"A charge of {charge} is more than the {capacity} the equipment holds.");

    public static Error ConcentrationOutOfRange(ProductType productType) =>
        Error.Validation(
            "Production.ConcentrationOutOfRange",
            $"{productType.Name} is made at {productType.MinConcentration} to {productType.MaxConcentration}.");

    public static Error NotEnoughConcentrate(Mass asked, Mass remaining) =>
        Error.Conflict("Production.NotEnoughConcentrate", $"The batch has {remaining} left, not {asked}.");

    public static Error ProductTypeNotFound(string id) =>
        Error.NotFound("Production.ProductTypeNotFound", $"No product type with ID '{id}' exists.");

    public static Error BatchNotFound(Guid id) =>
        Error.NotFound("Production.BatchNotFound", $"No concentrate batch with ID '{id}' exists.");

    public static Error RunNotFound(Guid id) =>
        Error.NotFound("Production.RunNotFound", $"No product run with ID '{id}' exists.");
}
