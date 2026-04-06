namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 主机版本检查器
/// </summary>
public class HostVersionChecker
{
    private readonly Version _hostVersion;

    public HostVersionChecker(string hostVersion)
    {
        _hostVersion = Version.Parse(hostVersion);
    }

    public bool IsCompatible(HostVersionRequirement requirement)
    {
        if (requirement == null)
        {
            return true;
        }

        // 检查最小版本
        if (!string.IsNullOrEmpty(requirement.Min))
        {
            var minVersion = Version.Parse(requirement.Min);
            if (_hostVersion < minVersion)
            {
                return false;
            }
        }

        // 检查最大版本
        if (!string.IsNullOrEmpty(requirement.Max))
        {
            var maxVersion = Version.Parse(requirement.Max);
            if (_hostVersion > maxVersion)
            {
                return false;
            }
        }

        return true;
    }
}
