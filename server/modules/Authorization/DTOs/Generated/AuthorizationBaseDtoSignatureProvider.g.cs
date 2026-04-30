using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authorization.DTOs.Generated;

public sealed class AuthorizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PermissionDto"] = "sha256:62c57a54d767e8264f1e6c858aa6a4c7aefe83e77d0d76baa0ae92a988d6f16a",
            ["RoleDto"] = "sha256:0540b26961eeb31eb16b2eef053953aaf1236193b1a4037dd2ca3f44f54f1ca8",
            ["RolePermissionDto"] = "sha256:71e706f495bbdddc9ac8c14067863aedaa61c3cfe2942ed0b30f543eef0e784c",
            ["UserRoleDto"] = "sha256:34f4e30c1af956872f7c12955745e49ab72a22e35355878d17f01bc9b04e6916",
        };
}
