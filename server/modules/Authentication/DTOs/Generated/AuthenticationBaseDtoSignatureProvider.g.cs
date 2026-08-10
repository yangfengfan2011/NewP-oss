using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authentication.DTOs.Generated;

public sealed class AuthenticationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuthenticationSessionDto"] = "sha256:04c6ec6b86aee8dcc8fca13417eb8f2748ddd624ee164b563bc88c4b490a76b4",
        };
}
