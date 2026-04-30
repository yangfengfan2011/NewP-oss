using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.ConfigCenter.DTOs.Generated;

public sealed class ConfigCenterBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConfigItemDto"] = "sha256:cb449765debb6334d58c3f8fe3f7495d274e38551e934eb50cd3a524e11ad398",
            ["ValueListItemDto"] = "sha256:b197aad60f6522f90f542f308ee3b7157be2a682645d1d5bb2db870869dfbf86",
        };
}
