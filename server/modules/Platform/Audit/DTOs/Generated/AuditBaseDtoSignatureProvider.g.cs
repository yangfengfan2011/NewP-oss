using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Audit.DTOs.Generated;

public sealed class AuditBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BusinessChangeDto"] = "sha256:454556a6929b8200cc376f01d3a89d64e746644a0acb34699aff803bade290a9",
        };
}
