namespace ICP.Platform.Plugin.Contracts;

/// <summary>
/// 当前公开 Platform API Release 的锁步身份。
/// </summary>
public static class PlatformApiReleaseIdentity
{
    public const string Version = "1.0.0";
    public const string ReleaseDigest = "sha256:e6f17cb0ca9747b06c83d9803d7cc634cff32c3e1234306389bc106a6fc3e273";
    public const string StandardDataProfile = "standard-data.v1";
    public const string PersistenceContract = "ef-migrations.v1";
    public const string SqlServerProvider = "sqlServer";
    public const string StandardPluginBlueprintSchema = "standard-plugin-blueprint.schema.json";
}
