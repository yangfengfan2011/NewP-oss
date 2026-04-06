using ICP.Platform.ExternalPluginTemplate.DTOs;

namespace ICP.Platform.ExternalPluginTemplate.Services;

public class ExternalPluginStatusService : IExternalPluginStatusService
{
    public ExternalPluginStatusResponse GetStatus()
    {
        return new ExternalPluginStatusResponse
        {
            ModuleId = "ExternalPlugin",
            Message = "External plugin template is running."
        };
    }
}
