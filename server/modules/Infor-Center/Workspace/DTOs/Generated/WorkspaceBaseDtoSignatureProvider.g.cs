using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Workspace.DTOs.Generated;

public sealed class WorkspaceBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorkspaceFolderContentDto"] = "sha256:212343c1e7261f122c733f13b7cfab9eda46cf7f581dd67383ff15e8a202f9d9",
            ["WorkspaceFolderDto"] = "sha256:c5d0c848ddeeee4f9eb88da1ddb9cbffbf0c68f3af931395f354291ce8dc9474",
            ["WorkspaceSpaceDto"] = "sha256:ada2110ea4cfece35a4acecaa0a9cb7367a24608460908c8694df6aa39f574e9",
        };
}
