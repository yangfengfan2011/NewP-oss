using Xunit;

namespace ICP.Platform.Testing;

/// <summary>
/// 标注需要外部环境（SQL Server、真实浏览器、dev supervisor）才能运行的测试。
/// 环境变量未置为 <c>1</c> 时，xUnit 把它记为「已跳过」并显示原因。
/// </summary>
/// <remarks>
/// 不要改回在测试体开头写 <c>if (!enabled) return;</c>：静默 return 会被记成
/// 「通过」，于是一层根本没执行过的验证，在输出里和真跑过的长得一模一样
/// （<c>已跳过: 0</c>）。跳过必须能被看见，否则它就是一个假的绿灯。
///
/// <see cref="FactAttribute.Skip"/> 在发现阶段求值，因此环境变量必须在
/// <c>dotnet test</c> 启动之前设置，进程内再改无效。
/// </remarks>
public sealed class EnvironmentGatedFactAttribute : FactAttribute
{
    public EnvironmentGatedFactAttribute(string variable, string requirement)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal))
        {
            Skip = $"需要 {variable}=1：{requirement}";
        }
    }
}
