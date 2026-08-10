using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Identity.DTOs.Generated;

public sealed class IdentityBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IdentitySecurityEventDto"] = "sha256:88c43b97ec964e70c8fef8af7d84b3caf6db7b53c5dd4dfd64e56e24a3a3d4d1",
            ["LocalCredentialDto"] = "sha256:742a12424ca9be8d17bed59b85d2dfaeed58f4e0013f834fdcb61242756b31e8",
            ["UserDto"] = "sha256:613c7782065d8419fac88d0fb568bd7cccb8ed34be25caa4ae0fb1bd5d699b2e",
        };
}
