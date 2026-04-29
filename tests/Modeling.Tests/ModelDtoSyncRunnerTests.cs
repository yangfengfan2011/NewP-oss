using ICP.Platform.Persistence.Composition;
using ICP.Platform.Tools.ModelDtoSync;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelDtoSyncRunnerTests
{
    [Fact]
    public void PlatformPersistenceModelCatalog_ReturnsProviderProjectedDefinitionsOnly()
    {
        var definitions = PlatformPersistenceModelCatalog.GetDefinitions();

        Assert.Contains(definitions, item => item.Name == "Role");
        Assert.Contains(definitions, item => item.Name == "Company");
        Assert.Contains(definitions, item => item.Name == "ValueListItem");
        Assert.Equal(definitions.Count, definitions.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Run_WritesGeneratedDtosWithModuleNamespaceAndStableSignatures()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();

        var writtenFiles = ModuleDtoSyncRunner.Run(repositoryRoot);

        var roleDtoPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Authorization",
            "DTOs",
            "Generated",
            "RoleDto.g.cs");
        var positionDtoPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Organization",
            "DTOs",
            "Generated",
            "PositionDto.g.cs");
        var signaturePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Authorization",
            "DTOs",
            "Generated",
            "AuthorizationBaseDtoSignatureProvider.g.cs");

        Assert.Contains(roleDtoPath, writtenFiles);
        Assert.Contains(signaturePath, writtenFiles);
        Assert.True(File.Exists(roleDtoPath));
        Assert.True(File.Exists(signaturePath));

        var roleDtoSource = File.ReadAllText(roleDtoPath);
        var positionDtoSource = File.ReadAllText(positionDtoPath);
        var signatureProviderSource = File.ReadAllText(signaturePath);
        var roleDtoSignature = ExtractSignature(signatureProviderSource, "RoleDto");

        Assert.StartsWith("namespace ICP.Platform.Modules.Authorization.DTOs;\n", roleDtoSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class RoleDto", roleDtoSource, StringComparison.Ordinal);
        Assert.Contains("using ICP.Platform.Modules.Organization.Models;\n", positionDtoSource, StringComparison.Ordinal);
        Assert.StartsWith(
            "using ICP.Platform.Modules.Organization.Models;\n\nnamespace ICP.Platform.Modules.Organization.DTOs;\n",
            positionDtoSource,
            StringComparison.Ordinal);
        Assert.Contains("public EmploymentStatus EmploymentStatus { get; init; }", positionDtoSource, StringComparison.Ordinal);
        Assert.Contains("public string? EmploymentStatus_DisplayValue { get; init; }", positionDtoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", roleDtoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", positionDtoSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", signatureProviderSource, StringComparison.Ordinal);
        Assert.Equal(DtoSignatureWriter.ComputeSignature(roleDtoSource), roleDtoSignature);
    }

    [Fact]
    public void ComputeSignature_NormalizesLineEndings()
    {
        var windowsSource = "namespace Demo;\r\n\r\npublic sealed class Sample\r\n{\r\n}\r\n";
        var linuxSource = "namespace Demo;\n\npublic sealed class Sample\n{\n}\n";

        Assert.Equal(
            DtoSignatureWriter.ComputeSignature(linuxSource),
            DtoSignatureWriter.ComputeSignature(windowsSource));
    }

    private static string CreateTemporaryRepositoryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "icp-model-dto-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ExtractSignature(string sourceCode, string dtoName)
    {
        var marker = $"[\"{dtoName}\"] = \"";
        var start = sourceCode.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"签名清单中缺少 {dtoName}。");

        start += marker.Length;
        var end = sourceCode.IndexOf('"', start);
        Assert.True(end > start, $"{dtoName} 签名格式不正确。");

        return sourceCode[start..end];
    }
}
