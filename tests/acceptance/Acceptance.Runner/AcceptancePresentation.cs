using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

/// <summary>呈现采集按需开启；功能旅程和失败诊断不依赖成功截图。</summary>
internal static class AcceptancePresentation
{
    internal static bool Enabled => Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_PRESENTATION") == "1";

    // 使用已就绪的真实页面采集，不为截图重放业务动作；调用者同时按 Enabled 选择呈现分支。
    internal static Task CaptureAsync(IPage page, string path) => Enabled
        ? page.ScreenshotAsync(new() { Path = path, FullPage = true, Animations = ScreenshotAnimations.Disabled })
        : Task.CompletedTask;
}
