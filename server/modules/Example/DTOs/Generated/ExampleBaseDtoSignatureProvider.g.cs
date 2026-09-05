using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Example.DTOs.Generated;

public sealed class ExampleBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ExampleRecordDto"] = "sha256:b4ac2f4209aceaa93055383bab09e56cbfb32872b5e08029c2f73b6e587731e8",
        };
}
