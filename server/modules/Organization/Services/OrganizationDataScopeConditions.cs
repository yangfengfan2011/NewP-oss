using System.Linq.Expressions;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>Organization 拥有当前部门状态事实；业务判断可用于动作，读取贡献明确缺失。</summary>
public sealed class OrganizationDataScopeConditions(IOrganizationQuery organizations) : IDataScopeBusinessConditionProvider
{
    public IReadOnlyList<DataScopeBusinessConditionDefinition> Definitions { get; } =
        [new("organization.person.departmentEnabled", "所属部门已启用", ["Organization/Person"], false)];

    /// <inheritdoc/>
    public void Validate(string key, JsonElement value)
    {
        if (key != "organization.person.departmentEnabled" || value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            throw new ArgumentException("所属部门已启用条件不接受参数。", nameof(value));
    }

    /// <inheritdoc/>
    public async Task<bool> MatchesAsync(string key, DataScopeResource resource, string objectId, JsonElement value,
        CancellationToken cancellationToken)
    {
        Validate(key, value);
        if (resource.ModelKey != "Organization/Person") throw new InvalidOperationException("DATA_SCOPE_BUSINESS_TARGET_MISMATCH");
        var view = await organizations.ReadAsync(cancellationToken);
        var person = view.Find(OrganizationRecordKind.Person, objectId);
        return person?["DepartmentId"] is string departmentId &&
            view.Find(OrganizationRecordKind.Department, departmentId)?["IsEnabled"] is true;
    }

    /// <inheritdoc/>
    public Expression BuildReadExpression(string key, DataScopeResource resource, ParameterExpression row, JsonElement value) =>
        throw new InvalidOperationException("DATA_SCOPE_READ_CONTRIBUTION_UNAVAILABLE");
}
