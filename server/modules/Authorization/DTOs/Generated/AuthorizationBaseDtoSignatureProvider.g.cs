using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authorization.DTOs.Generated;

public sealed class AuthorizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DataScopeRuleNodeDto"] = "sha256:de6d9df4f41c41e68b86288e27fab101e62cae6e04033e5b9914c8bda392c1a4",
            ["DataScopeRuleSubjectDto"] = "sha256:98f0e2213239d95b85f1f3941f386e2d4ec310c92ec6c9e929154f85b64b9ac0",
            ["FunctionPointDto"] = "sha256:1ac59a660947193da74217ef049ca8c5715fb6665f4469fd133f2ca0ee356296",
            ["PermissionDto"] = "sha256:008ec0471dde0e0f60bc128dba764ab6a5a83eaccba6d157c0d909c2411de4ad",
            ["RoleDto"] = "sha256:3923a698fade2a28dda81401b72cdeb31c2690dbdaf55f6977754ae5dd0546a3",
            ["RoleFunctionGrantDto"] = "sha256:6f16f5835bdd8d294597853f28842ac0b09dfa381a51b2e2f07cbe8a8d92cb80",
            ["RolePermissionDto"] = "sha256:d39e169e8ef3b3a8e266271e54c9e44943da2ae7c1f31d6ff5981ce979add6a6",
            ["UserRoleDto"] = "sha256:b8426a08429ab131fc5af61edac7b13a04ebc034e48c85339ca834e25b645476",
        };
}
