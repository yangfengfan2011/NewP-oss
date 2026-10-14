using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Document.DTOs.Generated;

public sealed class DocumentBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DocumentDto"] = "sha256:78682846a860450036969c2e2f69bbcbede1a33526f3c83a441b38e85ccd95d4",
            ["DocumentFileLinkDto"] = "sha256:c43e9f559ad87222cbfda8fa87341b3368182e423af0c040a486406562ba9bdc",
            ["DocumentIterationDto"] = "sha256:4285046b953ceb0b0b8b852c3c096fe012a85ec7c733598ff2da1a2ac1b76ab1",
            ["DocumentVersionDto"] = "sha256:8a27d28e2f3b43222541ecf691585a2c83d4f17be883c44d10ff1cc2a13c352c",
        };
}
