using ICP.Platform.Persistence.DevSync.Planning;

namespace ICP.Platform.Tools.DevSchemaSync.Execution;

public static class ConfirmationPrompts
{
    /// <summary>
    /// 第一次确认只回答“是否执行整次计划”，避免在用户尚未接受整体影响前就进入细节噪音。
    /// </summary>
    public static bool ConfirmApply(TextReader reader, TextWriter writer, SchemaPlan plan)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(plan);

        writer.WriteLine();
        writer.WriteLine("即将执行本次数据库结构同步计划。");
        writer.WriteLine($"- 计划操作数：{plan.Summary.OperationCount}");
        writer.WriteLine($"- 高风险操作数：{plan.Summary.ConfirmOperationCount}");

        return PromptYesNo(
            reader,
            writer,
            "是否继续执行本次同步计划？输入 y 继续，其他任意输入取消 [y/N]: ");
    }

    /// <summary>
    /// Confirm 风险需要强确认短语；这里故意不接受 y，避免用户在看完 SQL 前后误触高风险执行。
    /// </summary>
    public static bool ConfirmRisk(TextReader reader, TextWriter writer, IReadOnlyList<SchemaOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(operations);

        writer.WriteLine();
        writer.WriteLine("本次计划包含 Confirm 级高风险操作：");
        foreach (var operation in operations)
        {
            writer.WriteLine($"- {operation.Kind}: {operation.Description}");
        }

        writer.Write("请输入 APPLY RISK 继续执行高风险操作，其他任意输入取消: ");
        writer.Flush();

        var answer = reader.ReadLine();
        return string.Equals(answer, "APPLY RISK", StringComparison.Ordinal);
    }

    private static bool PromptYesNo(TextReader reader, TextWriter writer, string prompt)
    {
        writer.Write(prompt);
        writer.Flush();

        var answer = reader.ReadLine();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
