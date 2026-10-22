using ICP.Platform.Modules.ModuleTemplateSample.Controllers;
using ICP.Platform.Modules.ModuleTemplateSample.DTOs;
using ICP.Platform.Modules.ModuleTemplateSample.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.ModuleTemplateSample.Tests;

public class SampleStatusControllerTests
{
    [Fact]
    public void GetStatus_ReturnsOkResultWithCorrectShape()
    {
        // 准备
        var service = new SampleStatusService();
        var controller = new SampleStatusController(service);

        // 执行
        var result = controller.GetStatus();

        // 断言
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SampleStatusResponse>(okResult.Value);
        Assert.Equal("ModuleTemplateSample", response.ModuleId);
        Assert.Equal("模块模板示例", response.DisplayName);
        Assert.Equal("ModuleTemplateSample is ready.", response.Message);
    }
}
