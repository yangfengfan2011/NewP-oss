using System.Reflection;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityContractsArchitectureTests
{
    [Fact]
    public void PublicContracts_ExposeOnlyAccountDirectoryAndLifecycleParticipant()
    {
        var assembly = typeof(IIdentityAccountDirectory).Assembly;
        var publicTypes = assembly.GetExportedTypes();

        Assert.Contains(typeof(IIdentityAccountDirectory), publicTypes);
        Assert.Contains(typeof(IIdentityLifecycleParticipant), publicTypes);
        Assert.DoesNotContain(publicTypes, type => ContainsPersistenceLeak(type));
    }

    [Fact]
    public void PublicContractMethods_HaveXmlDocumentation()
    {
        var assembly = typeof(IIdentityAccountDirectory).Assembly;
        var documentation = LoadDocumentation(assembly);

        foreach (var method in assembly.GetExportedTypes()
                     .Where(static type => type.IsInterface)
                     .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
        {
            Assert.Contains($"name=\"M:{method.DeclaringType!.FullName}.{method.Name}", documentation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IdentityModule_DoesNotCompileGeneratedPrivateModelDtos()
    {
        var assembly = typeof(IdentityModule).Assembly;

        Assert.Null(assembly.GetType("ICP.Platform.Modules.Identity.DTOs.UserDto"));
        Assert.Null(assembly.GetType("ICP.Platform.Modules.Identity.DTOs.LocalCredentialDto"));
    }

    private static bool ContainsPersistenceLeak(Type type) =>
        TypeAndMembers(type).Any(candidate =>
            candidate.FullName?.Contains("EntityFrameworkCore", StringComparison.Ordinal) is true
            || candidate.Name.Contains("DbContext", StringComparison.Ordinal)
            || candidate.Name.Contains("Repository", StringComparison.Ordinal));

    private static IEnumerable<Type> TypeAndMembers(Type type)
    {
        yield return type;
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static string LoadDocumentation(Assembly assembly)
    {
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        Assert.True(File.Exists(path), $"Missing XML documentation file: {path}");
        return File.ReadAllText(path);
    }
}
