using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ICP.Platform.Modules.Organization;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationGeneratedDtoTests
{
    [Fact]
    public void DepartmentDto_UsesStringSortOrderAndExposesOwnedCompanyDisplayCompanion()
    {
        var dtoType = typeof(OrganizationModule).Assembly.GetType("ICP.Platform.Modules.Organization.DTOs.DepartmentDto");

        Assert.NotNull(dtoType);
        Assert.Equal(typeof(string), dtoType!.GetProperty("SortOrder")!.PropertyType);
        Assert.Equal(typeof(string), dtoType.GetProperty("CompanyId_DisplayValue")!.PropertyType);
        Assert.Null(dtoType.GetProperty("ParentDepartmentId_DisplayValue"));
    }

    [Fact]
    public void PersonDto_OnlyExposesDisplayCompanionOwnedByCompiledPlan()
    {
        var dtoType = typeof(OrganizationModule).Assembly.GetType("ICP.Platform.Modules.Organization.DTOs.PersonDto");

        Assert.NotNull(dtoType);
        var employmentStatusType = dtoType!.GetProperty("EmploymentStatus")!.PropertyType;
        Assert.Equal("EmploymentStatus", Nullable.GetUnderlyingType(employmentStatusType)!.Name);
        Assert.Equal(typeof(string), dtoType.GetProperty("CompanyId_DisplayValue")!.PropertyType);
        Assert.Null(dtoType.GetProperty("DepartmentId_DisplayValue"));
        Assert.Null(dtoType.GetProperty("PositionId_DisplayValue"));
        Assert.Null(dtoType.GetProperty("EmploymentStatus_DisplayValue"));
        Assert.NotNull(dtoType.GetProperty("Id"));
    }

    [Fact]
    public void SignatureProvider_ListsManagedOrganizationBaseDtos()
    {
        var signatures = GetSignatures(
            "ICP.Platform.Modules.Organization.DTOs.Generated.OrganizationBaseDtoSignatureProvider");

        AssertMatchesGeneratedFileSignatures("Organization", signatures);
    }

    private static IReadOnlyDictionary<string, string> GetSignatures(string providerTypeName)
    {
        var providerType = typeof(OrganizationModule).Assembly.GetType(providerTypeName);
        Assert.NotNull(providerType);

        var provider = Activator.CreateInstance(providerType!);
        var method = providerType!.GetMethod("GetSignatures", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(method!.Invoke(provider, null));
    }

    private static void AssertMatchesGeneratedFileSignatures(string moduleId, IReadOnlyDictionary<string, string> signatures)
    {
        var generatedDirectory = Path.Combine(GetRepositoryRoot(), "server", "modules", moduleId, "DTOs", "Generated");
        var expectedKeys = new[] { "CompanyDto", "DepartmentDto", "PersonDto", "PositionDto" };

        foreach (var key in expectedKeys)
        {
            Assert.True(signatures.TryGetValue(key, out var actualSignature), $"签名清单缺少 {key}。");
            var sourceCode = File.ReadAllText(Path.Combine(generatedDirectory, key + ".g.cs"));
            Assert.Equal(ComputeSignature(sourceCode), actualSignature);
        }
    }

    private static string ComputeSignature(string sourceCode)
    {
        var normalizedSourceCode = sourceCode.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSourceCode));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
