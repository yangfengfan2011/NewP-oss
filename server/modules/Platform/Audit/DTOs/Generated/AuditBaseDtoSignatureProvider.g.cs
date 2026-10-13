using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Audit.DTOs.Generated;

public sealed class AuditBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BusinessChangeDto"] = "sha256:c7c650df38983ef800fb0319f65bf2cd6602a26319384d944072263080b787ea",
        };
}
