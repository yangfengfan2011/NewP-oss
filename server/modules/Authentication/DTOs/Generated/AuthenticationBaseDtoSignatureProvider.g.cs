using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authentication.DTOs.Generated;

public sealed class AuthenticationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuthenticationSessionDto"] = "sha256:897208fbe47abf4d68c78015e13f311532a8f7ad42c1e03ec6b80607cc741907",
        };
}
