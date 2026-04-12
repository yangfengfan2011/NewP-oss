using ICP.Platform.Modules.MetadataConfigCenter.Models;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.MetadataConfigCenter.Controllers;

[ApiController]
[Route("metadata-config/modules")]
public class MetadataModulesController : ControllerBase
{
    private readonly IMetadataManifestService _metadataManifestService;

    public MetadataModulesController(IMetadataManifestService metadataManifestService)
    {
        _metadataManifestService = metadataManifestService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MetadataModuleManifest>>> GetModules(CancellationToken cancellationToken = default)
    {
        var manifests = await _metadataManifestService.GetModuleManifestsAsync(cancellationToken);
        return Ok(manifests);
    }
}
