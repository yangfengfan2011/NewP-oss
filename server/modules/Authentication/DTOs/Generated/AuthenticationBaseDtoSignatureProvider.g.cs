using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authentication.DTOs.Generated;

public sealed class AuthenticationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuthenticationSessionDto"] = "sha256:aa3d69ecc3c907cba129aa1aac09be7f1a4bac401571cf29133b58bcfabe8155",
        };
}
