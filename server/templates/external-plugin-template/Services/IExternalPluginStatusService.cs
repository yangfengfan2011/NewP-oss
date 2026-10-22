using ICP.Platform.ExternalPluginTemplate.DTOs;

namespace ICP.Platform.ExternalPluginTemplate.Services;

public interface IExternalPluginStatusService
{
    ExternalPluginStatusResponse GetStatus();
}
