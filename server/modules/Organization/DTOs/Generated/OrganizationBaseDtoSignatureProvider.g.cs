using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Organization.DTOs.Generated;

public sealed class OrganizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyDto"] = "sha256:1bba5459838d4a4627dea207390a69672b4acf74a93b7033380a36c73ce5a83f",
            ["DepartmentDto"] = "sha256:00500461a3fbeb3834e4639d591abb428ba9cd634f6e333bfd70d87149289f6b",
            ["PersonDto"] = "sha256:cfe1ec0a90a95546f8e02622a7fb5e8a31590821cee9577a97af4c301581027a",
            ["PersonUserLinkDto"] = "sha256:35a30904cc26fcbaf0294147491276871b0a4d727700a0ad839b11d1cd6f38ca",
            ["PositionDto"] = "sha256:0856f112271ad36f6da30f5bbfcd6611d21c7dd2b7b7235f708174d695cf8768",
        };
}
