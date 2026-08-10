using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.ConfigCenter.DTOs.Generated;

public sealed class ConfigCenterBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConfigItemDto"] = "sha256:2eaea62528fb2514eeeb63e92746823f07e74f423a94f594bc7bf4abf302287a",
            ["ValueListItemDto"] = "sha256:d7992b9d0a4b8456fab99a34a9fde961fd503efd44db816b46562e8083086618",
        };
}
