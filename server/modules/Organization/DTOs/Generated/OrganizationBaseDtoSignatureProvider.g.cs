using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Organization.DTOs.Generated;

public sealed class OrganizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyDto"] = "sha256:aae82252f0e340e1806ce213c98fe3da8a94a21280313dbcc461cb296160be43",
            ["DepartmentDto"] = "sha256:232c868dc08d148863751853e7d2bc14f0fbb94b3c629ad52c6c39a2f90db7ca",
            ["PersonDto"] = "sha256:99e6ff8f9d4e9a83ed245786e4d4881a6dfe3bdb0eb535e829bffc585ab696d9",
            ["PositionDto"] = "sha256:d34816625bdd6fe517ad15a65ee5af401239e0fac3e29c21ec5dc0349ebb6c73",
        };
}
