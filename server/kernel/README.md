# Kernel

Kernel 是平台组合内核，负责把模块与插件声明编译为确定、可校验、可投影的运行时事实。

当前职责包括：

- Module Capsule 候选读取、Canonical Module Plan 编译和冲突拒绝；
- 插件包扫描、完整性校验、安装库存与运行时视图物化；
- 活动模块目录及兼容投影生成；
- 模块装载、依赖排序和宿主装配所需的基础设施；
- 为持久化、授权和 Shell 提供同一计划的稳定投影。

Kernel 不拥有产品业务规则，也不允许模块通过加载顺序覆盖冲突。公共模块作者接口进入 Contracts/SDK；Host 只组合这些能力，不复制内核判断。

模块契约见 `docs/architecture/module-contract.md`，插件生命周期术语见 `docs/contexts/plugin-platform/CONTEXT.md`。
