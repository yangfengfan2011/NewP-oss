using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Identity.DTOs.Generated;

public sealed class IdentityBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IdentityAccountDto"] = "sha256:13bd5ec86b21b36de902185af917ccc80b5be04965b953663d08e9f1a1c192e7",
            ["IdentitySecurityEventDto"] = "sha256:053a066a975ae2d744d5084c09784c340a90c55afebf4c92430552405eae08a1",
            ["LocalCredentialDto"] = "sha256:c0d8dd6e691c38cff798d5d0c5eaf52e784114eb44efe4370defbd219f8be9fd",
        };
}
