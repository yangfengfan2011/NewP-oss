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
            var personDtoPath = GeneratedPath(repositoryRoot, "Organization", "PersonDto.g.cs");
            var positionDtoPath = GeneratedPath(repositoryRoot, "Organization", "PositionDto.g.cs");
            var signaturePath = GeneratedPath(repositoryRoot, "Authorization", "AuthorizationBaseDtoSignatureProvider.g.cs");
            var authenticationSessionDtoPath = GeneratedPath(repositoryRoot, "Authentication", "AuthenticationSessionDto.g.cs");
            var userDtoPath = GeneratedPath(repositoryRoot, "Identity", "IdentityAccountDto.g.cs");
            var securityEventDtoPath = GeneratedPath(repositoryRoot, "Identity", "IdentitySecurityEventDto.g.cs");

            Assert.Contains(roleDtoPath, writtenFiles);
            Assert.Contains(personDtoPath, writtenFiles);
            Assert.Contains(positionDtoPath, writtenFiles);
            Assert.Contains(signaturePath, writtenFiles);
            Assert.Contains(authenticationSessionDtoPath, writtenFiles);
            Assert.Contains(userDtoPath, writtenFiles);
            Assert.Contains(securityEventDtoPath, writtenFiles);
            foreach (var (module, model) in new[] { ("Workspace", "WorkspaceFolder"), ("Part", "Part"), ("Document", "Document") })
            {
                var generated = File.ReadAllText(Path.Combine(repositoryRoot, "server", "modules", "Infor-Center", module, "DTOs", "Generated", model + "Dto.g.cs"));
                Assert.Contains("public string? OwnerId { get; init; }", generated, StringComparison.Ordinal);
                Assert.Contains("public string? OwnerDepartmentId_DisplayValue { get; init; }", generated, StringComparison.Ordinal);
            }
            Assert.True(File.Exists(roleDtoPath));
            Assert.True(File.Exists(personDtoPath));
            Assert.True(File.Exists(positionDtoPath));
            Assert.True(File.Exists(signaturePath));
            Assert.True(File.Exists(authenticationSessionDtoPath));
            Assert.True(File.Exists(userDtoPath));
            Assert.True(File.Exists(securityEventDtoPath));

            var roleDtoSource = File.ReadAllText(roleDtoPath);
            var personDtoSource = File.ReadAllText(personDtoPath);
            var positionDtoSource = File.ReadAllText(positionDtoPath);
            var signatureProviderSource = File.ReadAllText(signaturePath);
            var securityEventDtoSource = File.ReadAllText(securityEventDtoPath);
            var roleDtoSignature = ExtractSignature(signatureProviderSource, "RoleDto");

            Assert.StartsWith("#nullable enable annotations\n\nnamespace ICP.Platform.Modules.Authorization.DTOs;\n", roleDtoSource, StringComparison.Ordinal);
            Assert.Contains("public sealed class RoleDto", roleDtoSource, StringComparison.Ordinal);
            Assert.Contains("using ICP.Platform.Modules.Organization.Models;\n", personDtoSource, StringComparison.Ordinal);
            Assert.StartsWith(
                "#nullable enable annotations\n\nusing ICP.Platform.Modules.Organization.Models;\n\nnamespace ICP.Platform.Modules.Organization.DTOs;\n",
                personDtoSource,
                StringComparison.Ordinal);
            Assert.Contains("public EmploymentStatus? EmploymentStatus { get; init; }", personDtoSource, StringComparison.Ordinal);
            Assert.StartsWith(
                "#nullable enable annotations\n\nnamespace ICP.Platform.Modules.Organization.DTOs;\n",
                positionDtoSource,
                StringComparison.Ordinal);
            Assert.Contains("public bool IsEnabled { get; init; } = true;", positionDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("CompanyId_DisplayValue", positionDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("DepartmentId_DisplayValue", positionDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("EmploymentStatus_DisplayValue", positionDtoSource, StringComparison.Ordinal);
            Assert.Contains("public string? OccurredAt_DisplayValue { get; init; }", securityEventDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("ActorKind_DisplayValue", securityEventDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", roleDtoSource, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", personDtoSource, StringComparison.Ordinal);
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
    public void Check_WhenGeneratedOutputsOnlyDifferByLineEndings_ReturnsNoDrift()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot();
        try
        {
            var writtenFiles = ModuleDtoSyncRunner.Run(repositoryRoot);
            foreach (var path in writtenFiles)
            {
                var source = File.ReadAllText(path);
                File.WriteAllText(path, source.Replace("\n", "\r\n", StringComparison.Ordinal));
            }

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
            var missingPath = GeneratedPath(repositoryRoot, "Identity", "IdentityAccountDto.g.cs");
            var unexpectedPath = GeneratedPath(repositoryRoot, "Authorization", "UnexpectedDto.g.cs");
            File.WriteAllText(changedPath, "drifted");
            File.Delete(missingPath);
            File.WriteAllText(unexpectedPath, "unexpected");

            var driftedFiles = ModuleDtoSyncRunner.Check(repositoryRoot);

            Assert.Contains("server/modules/Platform/Authorization/DTOs/Generated/RoleDto.g.cs", driftedFiles);
            Assert.Contains("server/modules/Platform/Authorization/DTOs/Generated/UnexpectedDto.g.cs", driftedFiles);
            Assert.Contains("server/modules/Platform/Identity/DTOs/Generated/IdentityAccountDto.g.cs", driftedFiles);
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
        return Path.Combine(repositoryRoot, "server", "modules", "Platform", moduleId, "DTOs", "Generated", fileName);
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
