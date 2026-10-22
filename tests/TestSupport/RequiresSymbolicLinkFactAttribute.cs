using Xunit;

namespace ICP.Platform.Testing;

/// <summary>
/// 标注需要创建符号链接的测试。Windows 上这需要开发者模式或
/// SeCreateSymbolicLinkPrivilege；权限不足时由 xUnit 记为「已跳过」。
/// </summary>
/// <remarks>
/// 与 <see cref="EnvironmentGatedFactAttribute"/> 的区别：这里探测的是能力
/// 而不是显式开关，所以有权限的机器照常执行，不需要谁记得去设环境变量。
///
/// 不要改回在测试体里 <c>catch (IOException) { return; }</c>：静默 return 会被
/// 记成通过，于是这条护栏在没有权限的机器上永远是假绿灯。
/// </remarks>
public sealed class RequiresSymbolicLinkFactAttribute : FactAttribute
{
    public RequiresSymbolicLinkFactAttribute()
    {
        if (!CanCreateSymbolicLink())
        {
            Skip = "需要创建符号链接的权限（Windows 开发者模式或提升的终端）。";
        }
    }

    private static bool CanCreateSymbolicLink()
    {
        var probe = Directory.CreateTempSubdirectory("newp-symlink-probe-");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(probe.FullName, "target"));
            Directory.CreateSymbolicLink(Path.Combine(probe.FullName, "link"), target.FullName);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            try
            {
                probe.Delete(recursive: true);
            }
            catch (IOException)
            {
                // 探测目录残留不影响结论。
            }
        }
    }
}
