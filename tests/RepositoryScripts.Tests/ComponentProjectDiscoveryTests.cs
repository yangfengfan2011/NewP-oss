using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class ComponentProjectDiscoveryTests
{
    [Theory]
    [InlineData("Example",
        new[] { "Example/Example.csproj", "Example/Migrations.SqlServer/Example.SqlServerMigrations.csproj" },
        new[] { "Example/Demo/DemoModule.csproj", "Example/ModuleTemplateSample/ModuleTemplateSample.csproj" })]
    [InlineData("Organization",
        new[] { "Platform/Organization/OrganizationModule.csproj", "Platform/Organization/Contracts/Organization.Contracts.csproj", "Platform/Organization/Migrations.SqlServer/Organization.SqlServerMigrations.csproj" },
        new[] { "Platform/Identity/IdentityModule.csproj", "Platform/Identity/Contracts/Identity.Contracts.csproj" })]
    [InlineData("Workspace",
        new[] { "Infor-Center/Workspace/WorkspaceModule.csproj", "Infor-Center/Workspace/Migrations.SqlServer/Workspace.SqlServerMigrations.csproj" },
        new[] { "Infor-Center/Part/PartModule.csproj", "Infor-Center/Document/DocumentModule.csproj" })]
    public async Task ExcludingComponent_RemovesOnlyItsOwnedProjects(
        string excludedModuleId, string[] excludedProjects, string[] retainedProjects)
    {
        // 直接验证 restore/build 之前的求值结果，不构建 Catalog 或任何模块工程。
        var references = await EvaluateHostProjectReferences(excludedModuleId);

        foreach (var project in excludedProjects)
            Assert.DoesNotContain("server/modules/" + project, references);
        foreach (var project in retainedProjects)
            Assert.Contains("server/modules/" + project, references);
    }

    private static async Task<string[]> EvaluateHostProjectReferences(string excludedModuleId)
    {
        var root = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "msbuild", "apps/host-api/HostApi.csproj", "-nologo",
                     "-getItem:ProjectReference", "-p:NewpExcludedModuleIds=" + excludedModuleId })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("Items").GetProperty("ProjectReference").EnumerateArray()
            .Select(item => Path.GetRelativePath(root, item.GetProperty("FullPath").GetString()!).Replace('\\', '/'))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("NewP repository not found.");
    }
}
