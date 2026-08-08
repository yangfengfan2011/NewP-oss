using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

internal static class PlatformApiReleaseIntegrity
{
    public static bool HasRequiredCanonicalSchema(IReadOnlyDictionary<string, string>? schemas) =>
        schemas is not null
        && schemas.Count == 1
        && schemas.TryGetValue(PlatformApiReleaseIdentity.StandardPluginBlueprintSchema, out var hash)
        && IsSha256(hash);

    public static bool IsSha256(string? value) =>
        value is not null
        && value.Length == 71
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value.AsSpan(7).ToArray().All(Uri.IsHexDigit);
}
