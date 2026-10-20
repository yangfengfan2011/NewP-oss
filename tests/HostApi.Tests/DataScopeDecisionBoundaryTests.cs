using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>按实际调用指令审计底层裁决消费者；动作改成变量或常量不会逃过调用边界。</summary>
public sealed class DataScopeDecisionBoundaryTests
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    [Fact]
    public void ProductionDecisionCalls_HaveAnExactOwnerAndMigrationReason()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var assemblies = candidates.Where(candidate => candidate.BackendEntry is not null).Select(candidate =>
                Assembly.LoadFrom(Path.GetFullPath(Path.Combine(candidate.PackageRoot, candidate.BackendEntry!))))
            .Append(typeof(StandardCrudDefaultWriter).Assembly).Distinct().ToArray();
        var calls = FindCalls(assemblies).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(calls);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var json = JsonSerializer.Serialize(calls, new JsonSerializerOptions { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_DECISION_CALL_INVENTORY") is { } file)
            File.WriteAllText(Path.Combine(root!.FullName, ".codex-temp", "issue-305", file), json);
        var inventoryFile = Path.Combine(root!.FullName, "tests", "HostApi.Tests", "Fixtures", "DataScopeDecisionCalls.json");
        Assert.True(File.Exists(inventoryFile), json);
        var inventory = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(inventoryFile))!;
        Assert.All(inventory, item => Assert.False(string.IsNullOrWhiteSpace(item.Value)));
        Assert.Equal(inventory.Keys.Order(StringComparer.Ordinal), calls);
    }

    [Fact]
    public void CompleteReadBoundary_IsAnActuallyRegisteredResolverConsumedByItsFixedInternalContract()
    {
        var services = new ServiceCollection();
        new WorkspaceModule().ConfigureServices(services);
        var registration = Assert.Single(services, service => service.ImplementationType == typeof(WorkspacePermissionTargetReadResolver));
        Assert.Equal(typeof(IDataExecutionTargetResolver), registration.ServiceType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        var result = new CanonicalModulePlanCompiler().Compile("complete-read-boundary",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var owner = Assert.Single(result.Plan!.Modules, module => module.ModuleId == "Workspace");
        var execution = Assert.Single(owner.DataExecutions, item => item.Key == WorkspacePermissionTargetReadResolver.Execution);
        Assert.Equal("internal", execution.Boundary);
        var source = Assert.Single(execution.FixedBindings);
        Assert.Equal("ICP.Platform.Modules.Workspace.WorkspaceContents.ResolveTargetsAsync", source.Caller);
        var role = Assert.Single(execution.Requirements);
        Assert.Equal("workspace.permission.target.fullRead", role.ReadResolverKey);
        Assert.Equal("read", Assert.Single(Assert.Single(role.AnyOf)));
        Assert.Contains(FindCalls([typeof(WorkspaceModule).Assembly]), call =>
            call == typeof(WorkspacePermissionTargetReadResolver).FullName + ".ResolveAsync[0] -> IsAllowedAsync");
    }

    [Fact]
    public void BoundaryScanner_FindsVariableAndConstantActionsAtTheCallSite()
    {
        var calls = FindCalls([typeof(DataScopeDecisionBoundaryTests).Assembly]).ToArray();
        Assert.Contains(calls, call => call.Contains(nameof(VariableAction), StringComparison.Ordinal));
        Assert.Contains(calls, call => call.Contains(nameof(ConstantAction), StringComparison.Ordinal));
        Assert.Contains(calls, call => call.Contains(nameof(ConcreteAction), StringComparison.Ordinal));
    }

    private static Task<bool> VariableAction(IDataScopeObjectDecisionProvider decisions, string action) => decisions.IsAllowedAsync("test.resource", "id", action);
    private static Task<bool> ConstantAction(IDataScopeObjectDecisionProvider decisions) => decisions.IsAllowedAsync("test.resource", "id", "edit");
    private static Task<bool> ConcreteAction(ConcreteDecisions decisions, string action) => decisions.IsAllowedAsync("test.resource", "id", action);

    private sealed class ConcreteDecisions : IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static IEnumerable<string> FindCalls(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetTypes())
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var il = method.GetMethodBody()?.GetILAsByteArray();
                    if (il is null) continue;
                    var offset = 0;
                    var callIndex = 0;
                    while (offset < il.Length)
                    {
                        var value = (short)il[offset++];
                        if (value == 0xfe) value = (short)(0xfe00 | il[offset++]);
                        var code = OpCodesByValue[value];
                        if (code.OperandType == OperandType.InlineMethod)
                        {
                            var token = BitConverter.ToInt32(il, offset);
                            var target = method.Module.ResolveMethod(token, type.IsGenericType ? type.GetGenericArguments() : null,
                                method.IsGenericMethod ? method.GetGenericArguments() : null);
                            if (target?.DeclaringType is { } targetType && typeof(IDataScopeObjectDecisionProvider).IsAssignableFrom(targetType)
                                && typeof(IDataScopeObjectDecisionProvider).GetMethods().Any(contract => contract.Name == target.Name))
                                yield return Owner(method) + "[" + callIndex++ + "] -> " + target.Name;
                        }
                        offset += code.OperandType switch
                        {
                            OperandType.InlineNone => 0,
                            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                            OperandType.InlineVar => 2,
                            OperandType.InlineI8 or OperandType.InlineR => 8,
                            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
                            _ => 4
                        };
                    }
                }
    }

    private static string Owner(MethodInfo method)
    {
        var type = method.DeclaringType!;
        if (type.DeclaringType is { } owner)
        {
            var source = owner.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(candidate => candidate.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType == type);
            if (source is not null) return CanonicalOwner(owner, source.Name);
        }
        return CanonicalOwner(type, method.Name);
    }

    private static string CanonicalOwner(Type type, string method)
    {
        if (method.StartsWith('<') && method.IndexOf('>') is var end && end > 1)
        {
            while (type.Name.StartsWith('<') && type.DeclaringType is not null) type = type.DeclaringType;
            return type.FullName + "." + method[1..end] + ".lambda";
        }
        return type.FullName + "." + method;
    }
}
