using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Authentication.DTOs.Generated;

public sealed class AuthenticationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuthenticationSessionDto"] = "sha256:85dd09990b248d46b78ac511c5938f911a4228ea6b9e1f9ba731691a7ea6cf53",
        };
}
