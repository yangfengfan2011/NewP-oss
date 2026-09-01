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
            ["PersonDto"] = "sha256:7e42d6e1972e5eaa871048fbf02e24dac82b9351b041d9447ecb075a3128d91a",
            ["PositionDto"] = "sha256:425a80b529fef658215799280afbec5aaeb0a8816ad9121b26c10807fc134782",
        };
}
