using ICP.Platform.Modules.ModuleTemplateSample.Services;
using Xunit;

namespace ICP.Platform.Modules.ModuleTemplateSample.Tests;

public class SampleStatusServiceTests
{
    [Fact]
    public void GetStatusMessage_ReturnsExpectedMessage()
    {
        // 准备
        var service = new SampleStatusService();

        // 执行
        var message = service.GetStatusMessage();

        // 断言
        Assert.Equal("ModuleTemplateSample is ready.", message);
    }
}
