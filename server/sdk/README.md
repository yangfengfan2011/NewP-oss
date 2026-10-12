# SDK

服务端 SDK 为模块行为实现提供薄适配和注册帮助器，不拥有模块身份或运行时状态。

## 标准内建模块入口

```csharp
using ICP.Platform.Sdk;

public sealed class MyModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "my-module.backend";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 只注册该模块的服务。
    }
}
```

`BehaviorKey` 必须与 `frontend/modules/my-module/newp.plugin.json` 的 `backend.behaviorKey` 完全一致。module id、显示名、版本、依赖和权限由嵌入的根 Capsule 提供，不在 C# 中重复声明。

## 放置边界

- 稳定跨进程或跨包数据形状进入 Contracts；
- 面向模块开发者的薄基类与 helper 进入 SDK；
- 装载、校验、冲突裁决、切换与回滚进入 Kernel；
- 可复用基础能力进入对应 `platform-*`；
- 业务服务、DTO 和数据访问实现留在模块内。

SDK 不得引用 Host 内部实现，也不得包含具体模块 ID 分支。

## 外部插件边界

本 SDK 面向仓内模块行为实现，不是外仓插件依赖。当前外部插件只使用精确版本的 `ICP.Platform.Plugin.Contracts`、`ICP.Platform.Plugin.Sdk`、`@icp-platform/plugin-sdk` 和 `ICP.Platform.Plugin.Tool`，并受 `standard-data.v1` 声明式 profile 约束。

## 推荐入口

- 内建模块：`scripts/new-module.ps1`；
- 后端模板：`server/templates/backend-module-template`；
- 外部插件：`docs/developer/external-plugin-development.md`；
- 外仓可执行基线：`tests/integration/plugin-api/baseline/1.0.0`。
