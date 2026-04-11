namespace ICP.Platform.Kernel.PluginPackages;

public static class InstalledPluginLifecycleState
{
    public const string Installed = "Installed";
    public const string Enabled = "Enabled";
    public const string Disabled = "Disabled";

    public static string FromRecord(InstalledPluginRecord record)
    {
        if (!string.Equals(record.Status, Installed, StringComparison.OrdinalIgnoreCase))
        {
            return record.Status;
        }

        if (!string.IsNullOrWhiteSpace(record.CurrentEnabledVersion))
        {
            return Enabled;
        }

        return record.IsDisabled ? Disabled : Installed;
    }
}
