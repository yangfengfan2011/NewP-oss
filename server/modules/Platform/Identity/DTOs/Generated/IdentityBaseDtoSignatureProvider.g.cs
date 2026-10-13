using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Identity.DTOs.Generated;

public sealed class IdentityBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IdentityAccountDto"] = "sha256:4efd05cd1a15f600a2713e0582dc2a58ea3e3f64c4d0134695ed4a15c0209569",
            ["IdentitySecurityEventDto"] = "sha256:237d12feb8f77d5d46406b42bb8f60e284989708483d780cdd5c490414866ae0",
            ["LocalCredentialDto"] = "sha256:aff8e144b86f72be489324f2ab31b4dd31c57c008be20ed8fafd08b6c7d97241",
        };
}
