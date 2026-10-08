using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Workspace.DTOs.Generated;

public sealed class WorkspaceBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorkspaceFolderContentDto"] = "sha256:15238a00a59c6b5427fdbc8351db9d4b1c2459f01be111a6e1c93474da0255fc",
            ["WorkspaceFolderDto"] = "sha256:c5d0c848ddeeee4f9eb88da1ddb9cbffbf0c68f3af931395f354291ce8dc9474",
            ["WorkspaceSpaceDto"] = "sha256:d5df5b77836e67e83be106df0c358a4421973a6630956a18d330af3948fa5603",
        };
}
