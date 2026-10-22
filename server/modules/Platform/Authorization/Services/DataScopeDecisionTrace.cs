using System.Linq.Expressions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>仅诊断请求收集裁决来源；正常权限请求不构造或执行额外投影。</summary>
internal sealed class DataScopeDecisionTrace(string objectId)
{
    private readonly Dictionary<string, string> paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<DataScopeSubjectDto>> subjects = new(StringComparer.Ordinal);
    private readonly List<DataScopeDecisionSourceDto> sources = [];
    private readonly List<(DataScopeRuleDto Node, string Opinion, Expression Match, Expression Effective)> readSources = [];
    internal string ObjectId => objectId;
    private string reason = "default";

    // 短路原因来自正式裁决所在分支，不能通过是否存在配置猜测。
    internal void SetReason(string value) => reason = value;

    // 路径来自已验证根树，不依赖前端的展开或排序状态。
    internal void SetRules(IEnumerable<DataScopeRuleDto> roots)
    {
        foreach (var root in roots) Visit(root, "");
        // 仅构造名称路径，不读取其他资源或条件事实。
        void Visit(DataScopeRuleDto node, string prefix)
        {
            paths[node.Id] = prefix + node.Name;
            foreach (var child in node.Children) Visit(child, paths[node.Id] + " / ");
        }
    }

    // 同优先级的全部明确票用于解释拒绝优先，低优先级不会被标成决定来源。
    internal void SetSubjects(string id, IReadOnlyList<DataScopeSubjectDto> selected) => subjects[id] = selected;

    // 完整动作路径已通过条件判断，只记录有明确意见的节点。
    internal void ObserveRule(DataScopeRuleDto node, string? opinion, bool effective)
    {
        if (opinion is null) return;
        sources.Add(new("rule", node.Id, paths[node.Id], opinion,
            effective ? "effective" : "descendantOverride", (subjects.GetValueOrDefault(node.Id) ?? []).Select(subject => new DataScopeDirectSubjectDto(
                subject.Id, subject.Type, subject.SubjectId, subject.Priority, subject.Actions, subject.MatchOptions)).ToArray()));
        reason = "rule";
    }

    // 读取树提供原始 SQL 条件和遮蔽后的 SQL 条件，避免用内存字符串比较解释数据库结果。
    internal void ObserveReadRule(DataScopeRuleDto node, string opinion, Expression match, Expression effective) =>
        readSources.Add((node, opinion, match, effective));

    // 固定批量投影避免每条规则一次 SQL 或单次生成无界列数；只读取指定对象的布尔结果。
    internal async Task ReadSourcesAsync(EfPlatformDbContext db, DataScopeResource resource,
        ParameterExpression row, CancellationToken token)
    {
        var target = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey)).AsNoTracking()
            .Where(item => EF.Property<string>(item, resource.IdField) == objectId);
        foreach (var batch in readSources.Chunk(50))
        {
            var projection = Expression.Lambda<Func<Dictionary<string, object?>, bool[]>>(
                Expression.NewArrayInit(typeof(bool), batch.SelectMany(item => new[] { item.Match, item.Effective })), row);
            var values = await target.Select(projection).SingleOrDefaultAsync(token);
            if (values is null) return;
            for (var i = 0; i < batch.Length; i++)
                if (values[i * 2]) ObserveRule(batch[i].Node, batch[i].Opinion, values[i * 2 + 1]);
        }
    }

    // 对象明确意见覆盖整棵规则结果，仍保留已经观察到的规则来源及覆盖说明。
    internal void ObserveObject(string opinion, IReadOnlyList<DataScopeDirectSubjectDto> selected)
    {
        for (var i = 0; i < sources.Count; i++) sources[i] = sources[i] with { Status = "objectOverride" };
        sources.Add(new("object", objectId, "对象授权", opinion, "effective", selected));
        reason = "object";
    }

    // 最终允许值由正式裁决返回，来源集合只负责解释。
    internal DataScopeActionExplanationDto Result(string key, string title, bool allowed) =>
        new(key, title, allowed, reason, sources);
}
