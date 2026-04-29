using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authorization.DTOs.Generated;

public sealed class AuthorizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PermissionDto"] = "sha256:9263d77d8f6db08c7e781c7e84f0babd8976ec1f9901103c5b8c3343c343aa89",
            ["RoleDto"] = "sha256:f1b1b5c488808babdba02335895f20c22201071f5c9c4b6ea8fcf8ac17651737",
            ["RolePermissionDto"] = "sha256:71e706f495bbdddc9ac8c14067863aedaa61c3cfe2942ed0b30f543eef0e784c",
            ["UserRoleDto"] = "sha256:34f4e30c1af956872f7c12955745e49ab72a22e35355878d17f01bc9b04e6916",
        };
}
