using Perfumery.Domain.Materials;

namespace Perfumery.Application.Abstractions.Materials;

/// <summary>The curated reference data of materials available to the player.</summary>
public interface IMaterialCatalog
{
    IReadOnlyList<Material> GetAll();

    Material? Find(MaterialId id);
}
