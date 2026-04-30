using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Organization.DTOs.Generated;

public sealed class OrganizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyDto"] = "sha256:c505e399c41fea2eb54c7d9c54d6a027ad8dce74b8794bccea996d28585cbd7d",
            ["DepartmentDto"] = "sha256:e765d452c0e3831043a16e2cc014c5f280b9d700982c066126a62e7e09f63529",
            ["PersonDto"] = "sha256:ec1de3bf402f43fe86d91f725619a99b37d8d8eb43d73deb9a3c04c570f03484",
            ["PersonUserLinkDto"] = "sha256:35a30904cc26fcbaf0294147491276871b0a4d727700a0ad839b11d1cd6f38ca",
            ["PositionDto"] = "sha256:aed0e369f82cc69534574b895cd62ecb182b365ae0643726e355610d428ca749",
        };
}
