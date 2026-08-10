using System.Reflection;
using ICP.Platform.Authentication.Contracts;
using Xunit;

namespace ICP.Platform.Authentication.Tests;

public sealed class AuthenticationContractsArchitectureTests
{
    [Fact]
    public void SessionRegistry_DoesNotExposeRawTokenOrPersistenceTypes()
    {
        var assembly = typeof(IAuthenticationSessionRegistry).Assembly;
        var publicTypes = assembly.GetExportedTypes();

        Assert.Contains(typeof(IAuthenticationSessionRegistry), publicTypes);
        Assert.DoesNotContain(publicTypes, type => type.Name.Contains("RawToken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicTypes, type => ContainsPersistenceLeak(type));
        Assert.DoesNotContain(publicTypes.SelectMany(PublicPropertyNames), name =>
            string.Equals(name, "Token", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "RawToken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublicContractMethods_HaveXmlDocumentation()
    {
        var assembly = typeof(IAuthenticationSessionRegistry).Assembly;
        var documentation = LoadDocumentation(assembly);

        foreach (var method in assembly.GetExportedTypes()
                     .Where(static type => type.IsInterface)
                     .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
        {
            Assert.Contains($"name=\"M:{method.DeclaringType!.FullName}.{method.Name}", documentation, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> PublicPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);

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
