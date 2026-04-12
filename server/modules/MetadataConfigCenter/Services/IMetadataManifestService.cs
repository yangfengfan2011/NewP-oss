using ICP.Platform.Modules.MetadataConfigCenter.Models;

namespace ICP.Platform.Modules.MetadataConfigCenter.Services;

public interface IMetadataManifestService
{
    Task<IReadOnlyList<MetadataModuleManifest>> GetModuleManifestsAsync(CancellationToken cancellationToken = default);
}
