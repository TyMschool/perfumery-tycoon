using Perfumery.SharedKernel;

namespace Perfumery.Domain.Materials;

public static class MaterialErrors
{
    public static readonly Error IdRequired =
        Error.Validation("Material.IdRequired", "A material ID is required.");

    public static readonly Error IdMalformed =
        Error.Validation("Material.IdMalformed", "A material ID must be lowercase words joined by hyphens.");

    public static readonly Error NameRequired =
        Error.Validation("Material.NameRequired", "A material needs a name.");

    public static readonly Error UnknownExtractionMethod =
        Error.Validation("Material.UnknownExtractionMethod", "The extraction method is not recognized.");

    public static readonly Error OdorStrengthOutOfRange =
        Error.Validation("Material.OdorStrengthOutOfRange", "Odor strength must be above 0.");

    public static Error NotFound(MaterialId id) =>
        Error.NotFound("Material.NotFound", $"No material with ID '{id.Value}' exists in the catalog.");
}
