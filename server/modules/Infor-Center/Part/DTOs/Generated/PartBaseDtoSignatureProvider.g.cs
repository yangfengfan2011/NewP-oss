using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Part.DTOs.Generated;

public sealed class PartBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PartDto"] = "sha256:1a617c188b02c0ed84b338103010aa4dcc9f4162bc21319868b82d2d665f75e7",
        };
}
