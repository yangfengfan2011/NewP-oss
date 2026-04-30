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
            ["PersonDto"] = "sha256:b7e03e7951e82e28bed1b5767a7705c296bab2908c7f1ec8603f83cdf1a03fec",
            ["PersonUserLinkDto"] = "sha256:35a30904cc26fcbaf0294147491276871b0a4d727700a0ad839b11d1cd6f38ca",
            ["PositionDto"] = "sha256:162a6c8ee3497a2132dc9785b06b1306ac37241a3ccd65cb0ad27df698f15dfd",
        };
}
