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

`BehaviorKey` 必须与 `modules/MyModule/newp.plugin.json` 的 `backend.behaviorKey` 完全一致。module id、显示名、版本、依赖和权限由嵌入的根 Capsule 提供，不在 C# 中重复声明。

## 放置边界

- 稳定跨进程或跨包数据形状进入 Contracts；
- 面向模块开发者的薄基类与 helper 进入 SDK；
- 装载、校验、冲突裁决、切换与回滚进入 Kernel；
- 可复用基础能力进入对应 `platform-*`；
- 业务服务、DTO 和数据访问实现留在模块内。

SDK 不得引用 Host 内部实现，也不得包含具体模块 ID 分支。

## 外部插件边界

外部插件模板当前只依赖公开 SDK 及其公开契约，不依赖 Host、Kernel 装配细节或内建模块实现。对外发布前仍需按插件版本与兼容契约单独验证。

## 推荐入口

- 内建模块：`scripts/new-module.ps1` 或 `scripts/new-module.sh`；
- 后端模板：`server/templates/backend-module-template`；
- 外部插件模板：`server/templates/external-plugin-template`；
- 打包：`scripts/pack-plugin.ps1` / `scripts/pack-plugin.sh`。
