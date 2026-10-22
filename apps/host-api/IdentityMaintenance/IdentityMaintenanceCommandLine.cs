namespace ICP.Platform.HostApi.IdentityMaintenance;

public enum IdentityMaintenanceCommandKind
{
    None,
    BootstrapLocalAdministrator,
    ResetLocalAdministrator
}

public static class IdentityMaintenanceCommandFailureCodes
{
    public const string InvalidArguments = "IDENTITY_MAINTENANCE_ARGUMENTS_INVALID";
    public const string InteractiveTerminalRequired = "IDENTITY_MAINTENANCE_INTERACTIVE_TERMINAL_REQUIRED";
    public const string Cancelled = "IDENTITY_MAINTENANCE_CANCELLED";
    public const string ConfigurationInvalid = "IDENTITY_LOCAL_ADMIN_CONFIGURATION_INVALID";
}

public readonly record struct IdentityMaintenanceCommandParseResult(
    bool IsMaintenanceCommand,
    IdentityMaintenanceCommandKind Kind,
    string? FailureCode);

public static class IdentityMaintenanceCommandLine
{
    public static IdentityMaintenanceCommandParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0 || !string.Equals(arguments[0], "identity", StringComparison.Ordinal))
        {
            return new(false, IdentityMaintenanceCommandKind.None, null);
        }

        if (arguments.Count != 2)
        {
            return new(true, IdentityMaintenanceCommandKind.None, IdentityMaintenanceCommandFailureCodes.InvalidArguments);
        }

        return arguments[1] switch
        {
            "bootstrap-local-admin" => new(true, IdentityMaintenanceCommandKind.BootstrapLocalAdministrator, null),
            "reset-local-admin" => new(true, IdentityMaintenanceCommandKind.ResetLocalAdministrator, null),
            _ => new(true, IdentityMaintenanceCommandKind.None, IdentityMaintenanceCommandFailureCodes.InvalidArguments)
        };
    }
}
