# SDK

服务端 SDK，为模块开发提供便利。

## 包含

- `ModuleBase` - 模块基类
- `ICP.Platform.Kernel.Abstractions` 中的公开模块契约
- 供模块注册服务、权限、菜单、配置声明使用的基础类型

## 对外开发边界

对外插件模板当前只允许依赖：

- `server/sdk`
- `server/kernel/Abstractions` 通过 `Sdk.csproj` 间接暴露的公开契约

不应依赖：

- `apps/host-api`
- `server/kernel` 的宿主装配实现细节
- 任意内建模块实现

## 使用方式

```csharp
using ICP.Platform.Sdk;

public class MyModule : ModuleBase
{
    public override string ModuleId => "MyModule";
    public override string DisplayName => "我的模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册服务
    }
}
```

## 当前建议

- 仓库内模块开发使用 `server/templates/backend-module-template`
- 外部插件开发使用 `server/templates/external-plugin-template`
- 打包通过 `scripts/pack-plugin.ps1` / `scripts/pack-plugin.sh`
