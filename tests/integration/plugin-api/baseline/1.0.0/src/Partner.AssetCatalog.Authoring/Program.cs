using ICP.Platform.Plugin.Contracts;
using ICP.Platform.Plugin.Sdk;

var root = args.Length == 1
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Expected the external consumer root.");
var compiled = new StandardPluginBlueprintCompiler().Compile(Path.Combine(root, "newp.plugin.json"));
if (!compiled.IsSuccess)
{
    throw new InvalidOperationException(string.Join(
        Environment.NewLine,
        compiled.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
}

var oldBytesOnNewHost = new PlatformApiCompatibilityEvaluator().Evaluate(
    compiled.Compilation!.Attestation,
    new PlatformApiHostCatalog(
        "1.1.0",
        [PlatformApiReleaseIdentity.StandardDataProfile],
        [
            new PlatformApiReleaseReference("1.0.0", PlatformApiReleaseIdentity.ReleaseDigest),
            new PlatformApiReleaseReference("1.1.0", $"sha256:{new string('b', 64)}")
        ]));
if (!oldBytesOnNewHost.IsCompatible)
{
    throw new InvalidOperationException("A same-major 1.1 Host rejected the original 1.0 package contract.");
}

var newBytesOnOldHost = new PlatformApiCompatibilityEvaluator().Evaluate(
    compiled.Compilation.Attestation with
    {
        BuiltWith = "1.1.0",
        MinInclusive = "1.1.0",
        ReleaseDigest = $"sha256:{new string('b', 64)}"
    },
    new PlatformApiHostCatalog(
        "1.0.0",
        [PlatformApiReleaseIdentity.StandardDataProfile],
        [new PlatformApiReleaseReference("1.0.0", PlatformApiReleaseIdentity.ReleaseDigest)]));
if (newBytesOnOldHost.IsCompatible
    || newBytesOnOldHost.Diagnostics.Single().Code != PlatformApiDiagnosticCodes.PlatformApiIncompatible)
{
    throw new InvalidOperationException("A 1.0 Host did not reject a 1.1-built package before activation.");
}

Console.WriteLine("Platform API external consumer contract passed.");
