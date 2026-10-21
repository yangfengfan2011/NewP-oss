using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>管理前值和缓存装载共用完整规则树物化；输出不暴露可变集合。</summary>
internal static class DataScopeRuleMaterializer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // 完整集合必须每个节点都可从规范根到达，主体也必须属于集合中的节点。
    internal static ImmutableArray<DataScopeRuleDto> Build(IEnumerable<IReadOnlyDictionary<string, object?>> nodes,
        IEnumerable<IReadOnlyDictionary<string, object?>> subjects)
    {
        var subjectRows = subjects.ToArray();
        var grouped = subjectRows.ToLookup(row => (string)row["NodeId"]!, StringComparer.Ordinal);
        var flat = nodes.Select(row => new DataScopeRuleDto((string)row["EID"]!, (string)row["RootId"]!, (string?)row["ParentId"],
            (string)row["Name"]!, (bool)row["Enabled"]!, (string)row["ResourceKey"]!,
            Freeze(JsonSerializer.Deserialize<DataScopeConditionDto>((string)row["ConditionJson"]!, Json)
                ?? throw new InvalidOperationException("DATA_SCOPE_CONDITION_INVALID")),
            grouped[(string)row["EID"]!].OrderBy(subject => (string)subject["EID"]!, StringComparer.Ordinal)
                .Select(subject => new DataScopeSubjectDto((string)subject["EID"]!, (string)subject["SubjectType"]!,
                    (string?)subject["SubjectId"], (long)subject["Priority"]!,
                    (JsonSerializer.Deserialize<Dictionary<string, string>>((string)subject["ActionsJson"]!, Json)
                        ?? throw new InvalidOperationException("DATA_SCOPE_SUBJECT_INVALID")).ToFrozenDictionary(StringComparer.Ordinal),
                    subject["MatchOptionsJson"] is string options ? JsonSerializer.Deserialize<DataScopeMatchOptions>(options, Json) : null))
                .ToImmutableArray(), [])).ToArray();
        var byId = flat.ToDictionary(node => node.Id, StringComparer.Ordinal);
        if (subjectRows.Any(subject => !byId.ContainsKey((string)subject["NodeId"]!)))
            throw new InvalidOperationException("DATA_SCOPE_SUBJECT_NODE_INVALID");
        var children = flat.Where(node => node.ParentId is not null).ToLookup(node => node.ParentId!, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var roots = flat.Where(node => node.ParentId is null && node.RootId == node.Id)
            .OrderBy(node => node.Id, StringComparer.Ordinal).Select(Tree).ToImmutableArray();
        if (visited.Count != flat.Length) throw new InvalidOperationException("DATA_SCOPE_TREE_INVALID");
        return roots;

        // 按直接父索引构树，避免每个节点再次扫描完整集合；停用分支也必须完整物化。
        DataScopeRuleDto Tree(DataScopeRuleDto node)
        {
            if (!visited.Add(node.Id)) throw new InvalidOperationException("DATA_SCOPE_TREE_INVALID");
            return node with { Children = children[node.Id].Where(child => child.RootId == node.RootId)
                .OrderBy(child => child.Id, StringComparer.Ordinal).Select(Tree).ToImmutableArray() };
        }
    }

    // JsonElement 保留独立只读值，嵌套条件集合也不能从已发布结果中改写。
    private static DataScopeConditionDto Freeze(DataScopeConditionDto? condition)
    {
        // JSON 可反序列化出 null 子项；明确识别结构错误，不能升级为无关资源的整体故障。
        if (condition is null) throw new InvalidOperationException("DATA_SCOPE_CONDITION_INVALID");
        return condition with
        {
            Value = condition.Value.ValueKind == JsonValueKind.Undefined ? condition.Value : condition.Value.Clone(),
            Children = condition.Children is null ? null : condition.Children.Select(Freeze).ToImmutableArray()
        };
    }
}
