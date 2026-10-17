using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Part.DTOs.Generated;

public sealed class PartBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PartDto"] = "sha256:16d083f47c74259c2b44347ce252d9c6f11cc54c1562b516c6d8bea2fe82f100",
        };
}
