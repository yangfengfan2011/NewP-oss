namespace ICP.Platform.Plugin.Contracts;

/// <summary>
/// Platform API Release 的稳定机器错误码。
/// </summary>
public static class PlatformApiDiagnosticCodes
{
    public const string PlatformApiReleaseSkew = "PLATFORM_API_RELEASE_SKEW";
    public const string PlatformApiIncompatible = "PLATFORM_API_INCOMPATIBLE";
    public const string PlatformApiAttestationMismatch = "PLATFORM_API_ATTESTATION_MISMATCH";
    public const string PackageSchemaUnsupported = "PACKAGE_SCHEMA_UNSUPPORTED";
    public const string PluginProfileUnsupported = "PLUGIN_PROFILE_UNSUPPORTED";
    public const string ProjectReferenceForbidden = "PROJECT_REFERENCE_FORBIDDEN";
    public const string WorkspaceDependencyForbidden = "WORKSPACE_DEPENDENCY_FORBIDDEN";
    public const string SourcePathDependencyForbidden = "SOURCE_PATH_DEPENDENCY_FORBIDDEN";
    public const string ForbiddenPlatformReference = "FORBIDDEN_PLATFORM_REFERENCE";
    public const string StandardProfileBackendCodeForbidden = "STANDARD_PROFILE_BACKEND_CODE_FORBIDDEN";
    public const string StandardProfileFrontendCodeForbidden = "STANDARD_PROFILE_FRONTEND_CODE_FORBIDDEN";
    public const string PersistenceContractUnsupported = "PERSISTENCE_CONTRACT_UNSUPPORTED";
    public const string PlatformApiDeprecated = "PLATFORM_API_DEPRECATED";
}
