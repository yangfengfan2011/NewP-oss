using System.Diagnostics;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.Plugin.Sdk;

namespace ICP.Platform.PluginApi.Tests;

public sealed class ExternalConsumerDependencyAuditorTests : IDisposable
{
    private readonly string _consumerRoot = Directory.CreateTempSubdirectory("newp_plugin_api_").FullName;

    [Fact]
    public void Audit_ProjectReferenceWorkspaceAndFileDependencies_ReturnsStableDiagnostics()
    {
        File.WriteAllText(
            Path.Combine(_consumerRoot, "Partner.AssetCatalog.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\NewP\server\sdk\Sdk.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(_consumerRoot, "package.json"),
            """
            {
              "devDependencies": {
                "@icp-platform/plugin-sdk": "workspace:*",
                "@icp-platform/legacy-sdk": "file:../NewP/frontend/packages/sdk"
              }
            }
            """);

        var diagnostics = new ExternalConsumerDependencyAuditor().Audit(_consumerRoot);

        Assert.Equal(
            new[]
            {
                PlatformApiDiagnosticCodes.ProjectReferenceForbidden,
                PlatformApiDiagnosticCodes.SourcePathDependencyForbidden,
                PlatformApiDiagnosticCodes.WorkspaceDependencyForbidden
            },
            diagnostics.Select(static diagnostic => diagnostic.Code).ToArray());
        Assert.All(diagnostics, static diagnostic =>
        {
            Assert.Equal("dependency-audit", diagnostic.Stage);
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.SourcePath));
        });
    }

    [Fact]
    public void Audit_PlatformRuntimePackages_ReturnsForbiddenPlatformReference()
    {
        File.WriteAllText(
            Path.Combine(_consumerRoot, "Partner.AssetCatalog.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="ICP.Platform.Kernel" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(_consumerRoot, "package.json"),
            """
            {
              "dependencies": {
                "@icp-platform/core": "1.0.0",
                "@icp-platform/ui": "1.0.0"
              }
            }
            """);

        var diagnostics = new ExternalConsumerDependencyAuditor().Audit(_consumerRoot);

        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, static diagnostic =>
            Assert.Equal(PlatformApiDiagnosticCodes.ForbiddenPlatformReference, diagnostic.Code));
        Assert.Equal(
            new[] { "@icp-platform/core", "@icp-platform/ui", "ICP.Platform.Kernel" },
            diagnostics.Select(static diagnostic => diagnostic.ResolvedPackageId).Order().ToArray());
    }

    [Fact]
    public void Audit_SourceDirectoryLinkOutsideConsumer_ReturnsSourcePathDependencyForbidden()
    {
        var outsideRoot = Directory.CreateTempSubdirectory("newp_plugin_source_").FullName;
        var linkPath = Path.Combine(_consumerRoot, "source-link");
        CreateDirectoryLink(linkPath, outsideRoot);
        try
        {
            var diagnostic = Assert.Single(new ExternalConsumerDependencyAuditor().Audit(_consumerRoot));

            Assert.Equal(PlatformApiDiagnosticCodes.SourcePathDependencyForbidden, diagnostic.Code);
            Assert.Equal(linkPath, diagnostic.SourcePath);
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }

            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath }
        }) ?? throw new InvalidOperationException("Failed to start mklink for the junction fixture.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }

    public void Dispose()
    {
        Directory.Delete(_consumerRoot, recursive: true);
    }
}
