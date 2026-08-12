using ICP.Platform.Persistence.Composition;
using ICP.Platform.Modeling.Rendering;
using ICP.Platform.Tools.ModelDtoSync;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelDtoSyncRunnerTests
{
    [Fact]
    public void PlatformPersistenceModelCatalog_ReturnsProviderProjectedDefinitionsOnly()
    {
        var definitions = NonSqlPlatformBaselineModelSource.GetDefinitions();

        Assert.Contains(definitions, item => item.Name == "Role");
        Assert.Contains(definitions, item => item.Name == "Company");
        Assert.Contains(definitions, item => item.Name == "ValueListItem");
        Assert.Equal(definitions.Count, definitions.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Run_WritesGeneratedDtosWithModuleNamespaceAndStableSignatures()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            var writtenFiles = ModuleDtoSyncRunner.Run(repositoryRoot);

            var roleDtoPath = GeneratedPath(repositoryRoot, "Authorization", "RoleDto.g.cs");
            var positionDtoPath = GeneratedPath(repositoryRoot, "Organization", "PositionDto.g.cs");
            var signaturePath = GeneratedPath(repositoryRoot, "Authorization", "AuthorizationBaseDtoSignatureProvider.g.cs");
            var authenticationSessionDtoPath = GeneratedPath(repositoryRoot, "Authentication", "AuthenticationSessionDto.g.cs");
            var userDtoPath = GeneratedPath(repositoryRoot, "Identity", "UserDto.g.cs");

            Assert.Contains(roleDtoPath, writtenFiles);
            Assert.Contains(signaturePath, writtenFiles);
            Assert.Contains(authenticationSessionDtoPath, writtenFiles);
            Assert.Contains(userDtoPath, writtenFiles);
            Assert.True(File.Exists(roleDtoPath));
            Assert.True(File.Exists(signaturePath));
            Assert.True(File.Exists(authenticationSessionDtoPath));
            Assert.True(File.Exists(userDtoPath));

            var roleDtoSource = File.ReadAllText(roleDtoPath);
            var positionDtoSource = File.ReadAllText(positionDtoPath);
            var signatureProviderSource = File.ReadAllText(signaturePath);
            var roleDtoSignature = ExtractSignature(signatureProviderSource, "RoleDto");

            Assert.StartsWith("#nullable enable annotations\n\nnamespace ICP.Platform.Modules.Authorization.DTOs;\n", roleDtoSource, StringComparison.Ordinal);
            Assert.Contains("public sealed class RoleDto", roleDtoSource, StringComparison.Ordinal);
            Assert.Contains("using ICP.Platform.Modules.Organization.Models;\n", positionDtoSource, StringComparison.Ordinal);
            Assert.StartsWith(
                "#nullable enable annotations\n\nusing ICP.Platform.Modules.Organization.Models;\n\nnamespace ICP.Platform.Modules.Organization.DTOs;\n",
                positionDtoSource,
                StringComparison.Ordinal);
            Assert.Contains("public EmploymentStatus EmploymentStatus { get; init; }", positionDtoSource, StringComparison.Ordinal);
            Assert.Contains("public string? CompanyId_DisplayValue { get; init; }", positionDtoSource, StringComparison.Ordinal);
            Assert.Contains("public string? DepartmentId_DisplayValue { get; init; }", positionDtoSource, StringComparison.Ordinal);
            Assert.Contains("public string? EmploymentStatus_DisplayValue { get; init; }", positionDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", roleDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", positionDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", signatureProviderSource, StringComparison.Ordinal);
            Assert.Equal(DtoSignatureWriter.ComputeSignature(roleDtoSource), roleDtoSignature);
        }
        finally
        {
            DeleteTemporaryRepositoryRoot(repositoryRoot);
        }
    }

    [Fact]
    public void Check_WhenGeneratedOutputsMatch_ReturnsNoDrift()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            ModuleDtoSyncRunner.Run(repositoryRoot);

            Assert.Empty(ModuleDtoSyncRunner.Check(repositoryRoot));
        }
        finally
        {
            DeleteTemporaryRepositoryRoot(repositoryRoot);
        }
    }

    [Fact]
    public void Check_ReportsMissingChangedAndUnexpectedFilesWithoutRewriting()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            ModuleDtoSyncRunner.Run(repositoryRoot);
            var changedPath = GeneratedPath(repositoryRoot, "Authorization", "RoleDto.g.cs");
            var missingPath = GeneratedPath(repositoryRoot, "Identity", "UserDto.g.cs");
            var unexpectedPath = GeneratedPath(repositoryRoot, "Authorization", "UnexpectedDto.g.cs");
            File.WriteAllText(changedPath, "drifted");
            File.Delete(missingPath);
            File.WriteAllText(unexpectedPath, "unexpected");

            var driftedFiles = ModuleDtoSyncRunner.Check(repositoryRoot);

            Assert.Contains("server/modules/Authorization/DTOs/Generated/RoleDto.g.cs", driftedFiles);
            Assert.Contains("server/modules/Authorization/DTOs/Generated/UnexpectedDto.g.cs", driftedFiles);
            Assert.Contains("server/modules/Identity/DTOs/Generated/UserDto.g.cs", driftedFiles);
            Assert.Equal("drifted", File.ReadAllText(changedPath));
            Assert.False(File.Exists(missingPath));
        }
        finally
        {
            DeleteTemporaryRepositoryRoot(repositoryRoot);
        }
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

    [Fact]
    public void ModelDtoSyncWriters_UseDevSyncSharedRenderingRules()
    {
        var sourceCode = "public sealed class RoleDto\r\n{\r\n}\r\n";
        var namespaceName = "ICP.Platform.Modules.Authorization.DTOs";

        Assert.Equal(
            BaseDtoSourceRenderer.WrapInNamespace(sourceCode, namespaceName),
            GeneratedCodeWriter.WrapInNamespace(sourceCode, namespaceName));
        Assert.Equal(
            BaseDtoSourceRenderer.ComputeSignature(sourceCode),
            DtoSignatureWriter.ComputeSignature(sourceCode));
    }

    private static string CreateTemporaryRepositoryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "icp-model-dto-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GeneratedPath(string repositoryRoot, string moduleId, string fileName)
    {
        return Path.Combine(repositoryRoot, "server", "modules", moduleId, "DTOs", "Generated", fileName);
    }

    private static void DeleteTemporaryRepositoryRoot(string repositoryRoot)
    {
        if (Directory.Exists(repositoryRoot))
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
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
