using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Modules.Workspace;

/// <summary>专用空间端点使用共享详情服务与正式显示投影，不向全局目录发布普通 CRUD 路由。</summary>
public sealed class WorkspaceFolderReadDescriptor
{
    public StandardCrudDescriptor Value { get; }

    // 复用标准 descriptor 编译器闭合真实计划中的 Model/display；局部目录不参与 HTTP 资源映射。
    public WorkspaceFolderReadDescriptor(CompiledPersistencePlan plan)
    {
        string[] fields = ["EID", "ENAME", "Description", "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
        Value = new StandardCrudResourceRegistry([new StandardCrudDescriptor
        {
            // 独立读取绑定真实详情声明；回执、初始化和API使用各自显式来源，不按缺声明选择旧路由。
            ResourceKey = "workspace.folder", ModuleId = "Workspace", ModelName = "WorkspaceFolder",
            Route = "api/workspace/folders", MapStandardEndpoints = false, IdField = "EID", DisplayField = "ENAME",
            FieldSet = new(fields, fields, [], []), SortableFields = ["ENAME"], DefaultSortField = "ENAME",
            Authorization = StandardCrudAuthorization.ByFeature("workspace.folder")
        }], plan).GetAll().Single();
    }
}
