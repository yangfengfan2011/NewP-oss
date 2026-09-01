using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Identity.DTOs.Generated;

public sealed class IdentityBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IdentityAccountDto"] = "sha256:5905e8a50b5c904d984637b300ba152b4a93c41c83c56e3959218c04ff9d107d",
            ["IdentitySecurityEventDto"] = "sha256:87654a5ae9ec03773409662d6a835b0afd107071a29d25c69518ecc838195ffd",
            ["LocalCredentialDto"] = "sha256:c0d8dd6e691c38cff798d5d0c5eaf52e784114eb44efe4370defbd219f8be9fd",
        };
}
