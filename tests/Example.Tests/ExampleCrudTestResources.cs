using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Modules.Example.Persistence.Descriptors;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Modules.Example.Tests;

internal static class ExampleCrudTestResources
{
    public static CompiledPersistencePlan Plan => new("candidate", "snapshot", "hash", default, "digest", [], ExampleModelDefinitions.All.Select(model => model with { OwnerModuleId = "Example" }).ToArray(), []);
    public static StandardCrudDescriptor Create() => new StandardCrudResourceRegistry([ExampleRecordCrudDescriptorFactory.Create()], Plan).GetAll().Single();
}
