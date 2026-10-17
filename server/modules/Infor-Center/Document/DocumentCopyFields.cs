using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;

namespace ICP.Platform.Modules.Document;

/// <summary>完整业务图的复制/重建分类边界；新增数据必须先作明确处理，不能静默落在副本外。</summary>
internal static class DocumentCopyFields
{
    private static readonly string[] EntityFields = ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
    private static readonly IReadOnlyDictionary<string, string[]> Classified = new Dictionary<string, string[]>(StringComparer.Ordinal) {
        ["Document"] = [.. EntityFields, "ECODE", "ENAME", "Description", "LatestVersionId", "OwnerId", "OwnerDepartmentId"],
        ["DocumentVersion"] = [.. EntityFields, "DocumentId", "VersionOrdinal", "VersionLabel", "State", "CurrentIterationId"],
        ["DocumentIteration"] = [.. EntityFields, "VersionId", "IterationOrdinal", "Kind", "DatasetTypeKey", "ECODE", "ENAME", "Description"],
        ["DocumentFileLink"] = ["LID", "PEID", "CEID", "LCreatedBy", "LCreatedAt", "LUpdatedBy", "LUpdatedAt", "Role", "Ordinal", "FileUsageId"] };

    // 可信正式注册对完整owner目录分类；新模型和新字段同样要求明确复制或重建规则。
    internal static void RequireAuthoring(IReadOnlyList<ModelDefinition> models)
    {
        if (!models.Select(model => model.LogicalKey).ToHashSet(StringComparer.Ordinal).SetEquals(Classified.Keys)) Fail();
        foreach (var model in models) Require(model.LogicalKey, model.Fields.Select(field => field.Name), false);
    }

    // 实际持久字段也封闭核对；Role的ordinal影子列仅是已有静态字段的内核派生，不是另一业务值。
    internal static void RequireRuntime(EfPlatformDbContext db)
    {
        foreach (var model in Classified.Keys) Require(model, db.Model.FindEntityType(model)!.GetProperties().Select(property => property.Name), true);
    }

    // 身份、审计和指针重新生成，当前业务值及文件集合由DocumentCopies完整处理。
    private static void Require(string model, IEnumerable<string> fields, bool runtime)
    {
        var expected = runtime && model == "DocumentFileLink" ? [.. Classified[model], "__ordinal_Role"] : Classified[model];
        if (!fields.ToHashSet(StringComparer.Ordinal).SetEquals(expected)) Fail();
    }

    // 固定失败信号不携带来源数据；字段演进不能依赖开发者记得补某个DTO。
    private static void Fail() => throw new InvalidOperationException("Document.CopyFieldClassificationRequired");
}
