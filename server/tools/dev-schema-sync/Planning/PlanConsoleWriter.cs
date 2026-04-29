using ICP.Platform.Persistence.DevSync.Planning;

namespace ICP.Platform.Tools.DevSchemaSync.Planning;

public static class PlanConsoleWriter
{
    /// <summary>
    /// 以稳定文本区块输出计划，方便开发者和后续 agent 在终端中快速确认差异范围。
    /// </summary>
    public static void Write(TextWriter writer, SchemaPlan plan)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(plan);

        writer.WriteLine("Summary");
        writer.WriteLine($"- Managed tables: {plan.Summary.ManagedTableCount}");
        writer.WriteLine($"- Planned operations: {plan.Summary.OperationCount}");
        writer.WriteLine($"- Safe operations: {plan.Summary.SafeOperationCount}");
        writer.WriteLine($"- Confirm operations: {plan.Summary.ConfirmOperationCount}");
        writer.WriteLine($"- Unmanaged tables: {plan.Summary.UnmanagedTableCount}");
        writer.WriteLine();

        writer.WriteLine("Managed Objects");
        if (plan.ManagedObjects.Count == 0)
        {
            writer.WriteLine("(none)");
        }
        else
        {
            foreach (var managedObject in plan.ManagedObjects)
            {
                writer.WriteLine($"- {managedObject}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("Planned Operations");
        if (plan.Operations.Count == 0)
        {
            writer.WriteLine("(none)");
        }
        else
        {
            foreach (var operation in plan.Operations)
            {
                writer.WriteLine($"- [{operation.RiskLevel}] {operation.Kind}: {operation.Description}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("Unmanaged Objects Summary");
        if (plan.UnmanagedTables.Count == 0)
        {
            writer.WriteLine("(none)");
            return;
        }

        writer.WriteLine($"Count: {plan.UnmanagedTables.Count}");
        foreach (var unmanagedTable in plan.UnmanagedTables.Take(10))
        {
            writer.WriteLine($"- {unmanagedTable}");
        }
    }
}
