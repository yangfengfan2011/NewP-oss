namespace ICP.Platform.Persistence.DevSync.Database;

/// <summary>
/// 数据库真实表快照，作为目标 schema diff 的只读输入；同名多 schema 冲突由计划阶段显式阻断。
/// </summary>
/// <param name="Schema">数据库 schema 名称，例如 dbo。</param>
/// <param name="Name">数据库表名。</param>
/// <param name="Columns">表内列快照，按数据库读取顺序提供给后续计划内核。</param>
public sealed record DatabaseTableSchema(
    string Schema,
    string Name,
    IReadOnlyList<DatabaseColumnSchema> Columns);
