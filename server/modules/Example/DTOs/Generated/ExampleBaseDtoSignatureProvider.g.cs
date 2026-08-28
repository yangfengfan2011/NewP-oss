using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Example.DTOs.Generated;

public sealed class ExampleBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ExampleRecordDto"] = "sha256:690edd5a0e1837f7f04e22e64fe8e6bab6bab0c813f1629347aa99d94d7fb049",
        };
}
