using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Modules.Organization.Tests;

internal static class OrganizationCrudTestResources
{
    public static StandardCrudDescriptor Resolve(StandardCrudDescriptor descriptor) =>
        new StandardCrudResourceRegistry([descriptor], new CompiledPersistencePlan(
            "candidate", "snapshot", "hash", default, "digest", [], OrganizationModelDefinitions.All.Select(model => model with { OwnerModuleId = "Organization" }).ToArray(), []))
            .GetAll().Single();
}
