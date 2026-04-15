using ICP.Platform.Tools.DevSchemaSync.Planning;

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
    /// 第二次确认只在存在 Confirm 风险时触发，并把高风险项显式展开，避免用户误以为只是普通增量同步。
    /// </summary>
    public static bool ConfirmHighRisk(TextReader reader, TextWriter writer, IReadOnlyList<SchemaOperation> confirmOperations)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(confirmOperations);

        writer.WriteLine();
        writer.WriteLine("检测到高风险操作，继续执行可能导致列删除、重命名或列定义收紧。");
        foreach (var operation in confirmOperations)
        {
            writer.WriteLine($"- {operation.Kind}: {operation.Description}");
        }

        return PromptYesNo(
            reader,
            writer,
            "是否确认继续执行以上高风险操作？输入 y 继续，其他任意输入取消 [y/N]: ");
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
