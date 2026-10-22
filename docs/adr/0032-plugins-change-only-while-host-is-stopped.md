---
status: accepted
---

# 插件变更只允许停服部署并在启动期生效

NewP 当前只支持在 Host 停止期间离线安装、升级、启用、停用、卸载或回滚插件，然后通过一次完整 Host / Shell 重启装载静态插件集合。Host 运行期间不接受任何会改变插件包、期望状态或活动状态的运维写操作；这些入口稳定返回 `501 Not Implemented` 与 `PLUGIN_RUNTIME_OPERATIONS_UNSUPPORTED`，插件运维前端不进入活动目录。只读 inventory 可以保留用于观察离线部署结果，但不表示运行时运维能力。

## Consequences

Canonical Module Plan、启动期校验、迁移、权限同步与投影仍用于构造单次启动的完整集合；Shell 也仍在启动时一次装载全部前端入口。当前支持场景中不会在进程存活期间切换插件集合，因此跨请求快照 identity gate、运行中原子切换、last-known-good 回滚和热更新恢复都不是当前完成条件，必须由未来独立插件运维设计重新定义失败域、并发、回滚和 Host / Shell 协调协议。

离线部署工具和包校验内核继续存在，但使用者必须遵守“停服、部署、重启”顺序。回滚本决定需要同时恢复受认证保护的运维入口、前端入口、并发与快照协议以及端到端 Gate，不能只把写 Controller 重新打开。
