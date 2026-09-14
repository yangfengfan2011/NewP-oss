using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Kernel.ModuleLoader;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace ICP.Platform.HostApi.Authorization;

public static class EndpointAccessFailureCodes
{
    public const string RuleMissing = "AUTH_ENDPOINT_RULE_MISSING";
    public const string RuleConflict = "AUTH_ENDPOINT_RULE_CONFLICT";
    public const string PermissionUnknown = "AUTH_PERMISSION_UNKNOWN";
    public const string PermissionModuleMismatch = "AUTH_PERMISSION_MODULE_MISMATCH";
    public const string PermissionCodeCollision = "AUTH_PERMISSION_CODE_COLLISION";
}

public sealed record EndpointAccessDiagnostic(
    string Code,
    string Endpoint,
    string Message);

public sealed record EndpointAccessPlan(
    IReadOnlyList<PermissionDescriptor> ActivePermissions);

public sealed class EndpointAccessPlanValidationException : InvalidOperationException
{
    public EndpointAccessPlanValidationException(
        IReadOnlyList<EndpointAccessDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            throw new ArgumentException("Endpoint access diagnostics cannot be empty.", nameof(diagnostics));
        }

        Diagnostics = diagnostics.ToArray();
    }

    public IReadOnlyList<EndpointAccessDiagnostic> Diagnostics { get; }

    private static string BuildMessage(IReadOnlyList<EndpointAccessDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Endpoint}: {diagnostic.Message}"));
    }
}

/// <summary>
/// 在 Host 监听前编译活动权限目录，并保证每个路由只有一个终态 access rule。
/// </summary>
public static class EndpointAccessPlanCompiler
{
    public static EndpointAccessPlan Compile(
        IEnumerable<EndpointDataSource> dataSources,
        ModuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(dataSources);
        ArgumentNullException.ThrowIfNull(registry);

        var diagnostics = new List<EndpointAccessDiagnostic>();
        var activePermissions = CompilePermissionCatalog(registry, diagnostics);
        var permissionsByCode = new Dictionary<string, PermissionDescriptor>(StringComparer.Ordinal);
        foreach (var permission in activePermissions)
        {
            permissionsByCode.TryAdd(permission.Code, permission);
        }

        foreach (var endpoint in dataSources
                     .SelectMany(static source => source.Endpoints)
                     .OrderBy(GetEndpointName, StringComparer.Ordinal))
        {
            ValidateEndpoint(endpoint, permissionsByCode, diagnostics);
        }

        if (diagnostics.Count > 0)
        {
            throw new EndpointAccessPlanValidationException(
                diagnostics
                    .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Endpoint, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
                    .ToArray());
        }

        return new EndpointAccessPlan(
            activePermissions
                .OrderBy(static permission => permission.Code, StringComparer.Ordinal)
                .ToArray());
    }

    private static IReadOnlyList<PermissionDescriptor> CompilePermissionCatalog(
        ModuleRegistry registry,
        ICollection<EndpointAccessDiagnostic> diagnostics)
    {
        var permissions = new List<PermissionDescriptor>();
        foreach (var pair in registry.GetAllModules().OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Value is not IPermissionProvider provider)
            {
                continue;
            }

            foreach (var permission in provider.GetPermissions())
            {
                permissions.Add(permission);
                if (!string.Equals(permission.ModuleId, pair.Key, StringComparison.Ordinal))
                {
                    diagnostics.Add(new EndpointAccessDiagnostic(
                        EndpointAccessFailureCodes.PermissionModuleMismatch,
                        $"catalog:{permission.Code}",
                        $"Permission owner '{permission.ModuleId}' does not match active module '{pair.Key}'."));
                }
            }
        }

        foreach (var collision in permissions
                     .GroupBy(static permission => permission.Code, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            diagnostics.Add(new EndpointAccessDiagnostic(
                EndpointAccessFailureCodes.PermissionCodeCollision,
                $"catalog:{collision.Key}",
                "Active permission codes collide by duplicate declaration or casing."));
        }

        return permissions;
    }

    // 功能位置是业务终态；既有 Host policy 作为附加约束共同满足，不替代位置判权。
    private static void ValidateEndpoint(
        Endpoint endpoint,
        IReadOnlyDictionary<string, PermissionDescriptor> permissionsByCode,
        ICollection<EndpointAccessDiagnostic> diagnostics)
    {
        var endpointName = GetEndpointName(endpoint);
        var allowAnonymous = endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>();
        var authenticatedOnly = endpoint.Metadata.GetOrderedMetadata<AuthenticatedOnlyAttribute>();
        var denyAll = endpoint.Metadata.GetOrderedMetadata<DenyAllAttribute>();
        var permissionRules = endpoint.Metadata.GetOrderedMetadata<RequirePermissionAttribute>();
        var featureRules = endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();
        var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var otherAuthorization = authorizeData
            .Where(static data => data is not AuthenticatedOnlyAttribute
                                  && data is not DenyAllAttribute
                                  && data is not RequirePermissionAttribute
                                  && data is not RequireFeatureAttribute)
            .ToArray();
        var hostPolicies = otherAuthorization
            .Where(static data => !string.IsNullOrWhiteSpace(data.Policy))
            .ToArray();
        var bareAuthorization = otherAuthorization
            .Where(static data => string.IsNullOrWhiteSpace(data.Policy))
            .ToArray();

        var terminalRuleCount = allowAnonymous.Count
                                + authenticatedOnly.Count
                                + denyAll.Count
                                + permissionRules.Count
                                + featureRules.Count
                                + (featureRules.Count == 0 ? hostPolicies.Length : 0);
        var conflict = terminalRuleCount > 1
                       || (allowAnonymous.Count > 0 && authorizeData.Count > 0)
                       || (bareAuthorization.Length > 0 && terminalRuleCount > 0);
        if (conflict)
        {
            diagnostics.Add(new EndpointAccessDiagnostic(
                EndpointAccessFailureCodes.RuleConflict,
                endpointName,
                "Endpoint must declare exactly one terminal access rule."));
        }
        else if (terminalRuleCount == 0 || bareAuthorization.Length > 0)
        {
            diagnostics.Add(new EndpointAccessDiagnostic(
                EndpointAccessFailureCodes.RuleMissing,
                endpointName,
                "Endpoint has no terminal access rule; bare Authorize is not sufficient."));
        }

        foreach (var permissionRule in permissionRules)
        {
            if (!permissionsByCode.TryGetValue(permissionRule.PermissionCode, out var permission))
            {
                diagnostics.Add(new EndpointAccessDiagnostic(
                    EndpointAccessFailureCodes.PermissionUnknown,
                    endpointName,
                    $"Permission '{permissionRule.PermissionCode}' is not in the active catalog."));
                continue;
            }

            var owners = endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
            if (owners.Count != 1
                || !string.Equals(owners[0].ModuleId, permission.ModuleId, StringComparison.Ordinal))
            {
                diagnostics.Add(new EndpointAccessDiagnostic(
                    EndpointAccessFailureCodes.PermissionModuleMismatch,
                    endpointName,
                    $"Permission '{permissionRule.PermissionCode}' is owned by '{permission.ModuleId}', but endpoint owner metadata does not match."));
            }
        }
    }

    private static string GetEndpointName(Endpoint endpoint)
    {
        if (endpoint is RouteEndpoint routeEndpoint)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            return $"{string.Join(',', methods)} /{routeEndpoint.RoutePattern.RawText?.TrimStart('/')}";
        }

        return endpoint.DisplayName ?? "(unknown endpoint)";
    }
}
