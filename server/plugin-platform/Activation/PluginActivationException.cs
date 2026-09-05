using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.PluginPlatform.Activation;

public static class PluginActivationFailureCodes
{
    public const string PackagePreflightFailed = "PLUGIN_ACTIVATION_PREFLIGHT_FAILED";
    public const string ModulePlanFailed = "PLUGIN_ACTIVATION_MODULE_PLAN_FAILED";
    public const string RuntimeMetadataValidationFailed = "PLUGIN_ACTIVATION_RUNTIME_METADATA_VALIDATION_FAILED";
    public const string PermissionCatalogFailed = "PLUGIN_ACTIVATION_PERMISSION_CATALOG_FAILED";
    public const string PersistencePlanFailed = "PLUGIN_ACTIVATION_PERSISTENCE_PLAN_FAILED";
    public const string MigrationFailed = "PLUGIN_ACTIVATION_MIGRATION_FAILED";
    public const string PermissionSyncFailed = "PLUGIN_ACTIVATION_PERMISSION_SYNC_FAILED";
    public const string RuntimeStagingFailed = "PLUGIN_ACTIVATION_RUNTIME_STAGING_FAILED";
    public const string RuntimeCommitFailed = "PLUGIN_ACTIVATION_RUNTIME_COMMIT_FAILED";
    public const string ActiveCommitFailed = "PLUGIN_ACTIVATION_ACTIVE_COMMIT_FAILED";
    public const string SessionAlreadyCompleted = "PLUGIN_ACTIVATION_SESSION_ALREADY_COMPLETED";
}

/// <summary>
/// 启动期激活失败的稳定异常。DatabaseAhead 为 true 时只允许重试续跑，禁止 Down。
/// </summary>
public sealed class PluginActivationException : InvalidOperationException
{
    public PluginActivationException(
        string failureCode,
        bool databaseAhead,
        Exception? innerException = null)
        : base(
            $"Plugin activation failed ({failureCode})."
            + (databaseAhead ? " Database may be ahead of the active snapshot." : string.Empty),
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        FailureCode = failureCode;
        DatabaseAhead = databaseAhead;
    }

    public string FailureCode { get; }

    public bool DatabaseAhead { get; }

    internal static PluginActivationException Wrap(
        string fallbackFailureCode,
        bool databaseAhead,
        Exception exception)
    {
        var failureCode = exception switch
        {
            PluginActivationException activationException => activationException.FailureCode,
            PersistencePlanExecutionException persistenceException => persistenceException.FailureCode,
            _ => fallbackFailureCode
        };
        var effectiveDatabaseAhead = databaseAhead
            || exception is PluginActivationException { DatabaseAhead: true };
        return new PluginActivationException(failureCode, effectiveDatabaseAhead, exception);
    }
}
