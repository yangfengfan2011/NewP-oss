using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.ConfigCenter.DTOs.Generated;

public sealed class ConfigCenterBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConfigItemDto"] = "sha256:b62377f432088b93084a4826cb4c66c8ad95f3a15fd640e087fdaa95dbbeef99",
            ["ValueListItemDto"] = "sha256:3ab3daeb9c978cb56aabd997c4b322f791e82af310d1bc92bcf6989461b2f213",
        };
}
