using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Files.DTOs.Generated;

public sealed class FilesBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FileCopyLeaseDto"] = "sha256:ca44031eb04d28dac1b038e26315b09dea9f5f04563d2897e7e1a0446985af15",
            ["FileRecordDto"] = "sha256:ff4245b5bc869a461dd95c4e1e315968abcc70fd4c47bed07697baf11ec78efc",
            ["FileUploadLeaseDto"] = "sha256:40ed151b8a7cc224ac411d1b490e2b5f84a7c10cdd0bc9791567d31f335211ca",
            ["FileUsageDto"] = "sha256:c104ee883edbac260719cd5917a9ea5dd6a2a2d1188aa5513124615d70a11b19",
        };
}
