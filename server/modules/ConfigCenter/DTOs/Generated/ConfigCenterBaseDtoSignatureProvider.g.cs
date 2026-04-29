using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.ConfigCenter.DTOs.Generated;

public sealed class ConfigCenterBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConfigItemDto"] = "sha256:5b3326ff3f931e0f97c69216001310dc26a2aa0318995512dab772f1263e000c",
            ["ValueListItemDto"] = "sha256:d1be2cd7ae78457f317fa9bec5078ac4fae8ca0394805bb6b5ad5d61aa561a7f",
        };
}
