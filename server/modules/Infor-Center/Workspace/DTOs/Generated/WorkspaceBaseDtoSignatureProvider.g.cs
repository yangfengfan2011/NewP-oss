using System;
using System.Collections.Generic;

namespace ICP.Platform.Modules.Workspace.DTOs.Generated;

public sealed class WorkspaceBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorkspaceFolderContentDto"] = "sha256:7b3db69640bacf278b6e3dcc6a7c172001c16adf96b426a84a773b50b1662de4",
            ["WorkspaceFolderDto"] = "sha256:c6d06826da379ba822873344beefbfd5410c682f763b3651a59995f567c94304",
            ["WorkspaceObjectCopyDto"] = "sha256:515ed7bf50598e858254559e9593a2f2d38252476432e405ec2c10cc79381cb1",
            ["WorkspaceSpaceDto"] = "sha256:ada2110ea4cfece35a4acecaa0a9cb7367a24608460908c8694df6aa39f574e9",
        };
}
