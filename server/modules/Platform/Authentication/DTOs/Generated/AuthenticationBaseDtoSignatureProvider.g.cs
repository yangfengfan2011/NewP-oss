using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authentication.DTOs.Generated;

public sealed class AuthenticationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuthenticationSessionDto"] = "sha256:8a5d9597b90e4a3b1d1adec7495e08fa22adfa3e63495d4b37088fa5d8b6ae56",
        };
}
