using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Example.DTOs.Generated;

public sealed class ExampleBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ExampleRecordDto"] = "sha256:09076b84943b24bc33aedf9fa1a87eb0cc67ca23673528b1d4ec8f87be7c257c",
        };
}
