using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

// 页面驱动（登录导航组）：持有 Shell 登录页与登录后落点的控件知识，不持有 actor 凭据或旅程顺序。
// 一个实例绑定一个页面与一个 Shell origin。
internal sealed class ShellPageDriver
{
    internal const string LoginButtonLabel = "登录";
    internal const string SignOutLabel = "退出登录";
    private const string LoginPath = "/login";

    internal ShellPageDriver(IPage page, string shellOrigin)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
        ShellOrigin = string.IsNullOrWhiteSpace(shellOrigin)
            ? throw new ArgumentException("Shell origin is required.", nameof(shellOrigin))
            : shellOrigin.TrimEnd('/');
    }

    internal IPage Page { get; }

    internal string ShellOrigin { get; }

    // 登录并等待进入已认证的 Shell 首页；受限账号（登录后被带到改密页）应改用 SubmitLoginAsync + WaitForLoginPageLeftAsync。
    internal async Task LoginAsync(string ecode, string password)
    {
        await SubmitLoginAsync(ecode, password);
        await WaitForAuthenticatedShellAsync();
    }

    // 仅提交登录表单：不在登录页时先导航过去，填写账号与密码后点击登录，不等待落点。
    internal async Task SubmitLoginAsync(string ecode, string password)
    {
        var alreadyOnLoginPage = Uri.TryCreate(Page.Url, UriKind.Absolute, out var current)
            && current.AbsolutePath.Equals(LoginPath, StringComparison.Ordinal);
        if (!alreadyOnLoginPage)
        {
            await Page.GotoAsync($"{ShellOrigin}{LoginPath}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        }

        await Page.Locator("#ecode").FillAsync(ecode);
        await Page.Locator("#password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = LoginButtonLabel, Exact = true }).ClickAsync();
    }

    // 已认证落点以回到 Shell 首页且退出登录入口可见为准。
    internal async Task WaitForAuthenticatedShellAsync()
    {
        await Page.WaitForURLAsync(
            $"{ShellOrigin}/",
            new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = SignOutLabel, Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    // 只确认离开登录页，不假定落点；用于受限会话等由业务决定去向的登录。
    internal Task WaitForLoginPageLeftAsync() =>
        Page.WaitForFunctionAsync($"() => window.location.pathname !== '{LoginPath}'");
}
