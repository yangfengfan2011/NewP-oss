using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authorization.DTOs.Generated;

public sealed class AuthorizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PermissionDto"] = "sha256:902176e4868cd3685c63c0ddd2630c240832d660d10aaed2304110a7587cdbaa",
            ["RoleDto"] = "sha256:30294ea00d5ec0ff94ff53b20aea49807f27ce9c4f4c9d28eb0a87ea45c567e9",
            ["RolePermissionDto"] = "sha256:deaf5268f58da3502ebd23a7da5836a762898d743ccb2e852959bc7099724147",
            ["UserRoleDto"] = "sha256:8bf5b7eecc65824554e109d050a44c435cd1c47a6cb94f5d6f7da444bc38849d",
        };
}
