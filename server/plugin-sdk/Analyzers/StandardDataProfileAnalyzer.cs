using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ICP.Platform.Plugin.Sdk.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StandardDataProfileAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "STANDARD_PROFILE_BACKEND_CODE_FORBIDDEN";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "standard-data.v1 forbids executable backend extension points",
        "Type or method '{0}' introduces backend runtime code, which standard-data.v1 does not allow",
        "NewP.PlatformApi",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Standard data plugins are declarative and cannot ship controllers, module entry points, or DI callbacks.");

    private static readonly ImmutableHashSet<string> ForbiddenBaseTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Microsoft.AspNetCore.Mvc.Controller",
        "Microsoft.AspNetCore.Mvc.ControllerBase",
        "ICP.Platform.Kernel.Abstractions.IModule",
        "ICP.Platform.Sdk.ModuleBase");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!EnumerateTypeHierarchy(type).Any(symbol => ForbiddenBaseTypes.Contains(symbol.ToDisplayString())))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations.FirstOrDefault(), type.Name));
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!method.Parameters.Any(parameter =>
                string.Equals(
                    parameter.Type.ToDisplayString(),
                    "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                    StringComparison.Ordinal)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations.FirstOrDefault(), method.Name));
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var contract in type.AllInterfaces)
        {
            yield return contract;
        }
    }
}
