using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Organization.DTOs.Generated;

public sealed class OrganizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyDto"] = "sha256:aae82252f0e340e1806ce213c98fe3da8a94a21280313dbcc461cb296160be43",
            ["DepartmentDto"] = "sha256:87c3419879411ee139c6f1902865c771261d9f65a9a4530ed64212fa7a87cc6f",
            ["PersonDto"] = "sha256:3f153257aa1159c1578839051db972f4bae6b84cfee64ebedcf5da99bc1d332d",
            ["PositionDto"] = "sha256:f2d2fd9035080ce7a782bc74806a9aa7bfd367f521e03d20d5f88c5d2c95dc58",
        };
}
