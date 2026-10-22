using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Organization.DTOs.Generated;

public sealed class OrganizationBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyDto"] = "sha256:41be4389b0dbde55460c657c32cad29446a5487553db8ea1a9559cf2a7b72c14",
            ["DepartmentDto"] = "sha256:0ed4ca9c99512e3e15a8aff19c09c11baa4f8c06dfed2913acf9910e16828dd4",
            ["PersonDto"] = "sha256:67ad07326196c746d97d9e6815ee75da1a062847221d097c7158eb22265c18b6",
            ["PositionDto"] = "sha256:2ac0fc2c4a65525d2eb08788da3e50dabd716d165ef607415eb54533afe35c3c",
        };
}
