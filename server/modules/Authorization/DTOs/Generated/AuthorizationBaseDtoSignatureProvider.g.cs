using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authorization.DTOs.Generated;

public sealed class AuthorizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PermissionDto"] = "sha256:008ec0471dde0e0f60bc128dba764ab6a5a83eaccba6d157c0d909c2411de4ad",
            ["RoleDto"] = "sha256:3923a698fade2a28dda81401b72cdeb31c2690dbdaf55f6977754ae5dd0546a3",
            ["RolePermissionDto"] = "sha256:d39e169e8ef3b3a8e266271e54c9e44943da2ae7c1f31d6ff5981ce979add6a6",
            ["UserRoleDto"] = "sha256:b8426a08429ab131fc5af61edac7b13a04ebc034e48c85339ca834e25b645476",
        };
}
