using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Document.DTOs.Generated;

public sealed class DocumentBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DocumentDto"] = "sha256:166f3c490a0c5b0c2e0886a0af4eae50e91483f1dda335e4cbfa4cd1e5e09ed1",
        };
}
