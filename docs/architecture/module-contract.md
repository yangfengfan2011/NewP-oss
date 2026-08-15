# Module Contract

## Authoring owners

- Module Capsule 根声明唯一拥有 module id、display name、version、host contract range 与 dependencies。
- 后端 C# behavior binding 只注册 DI、endpoint 与服务实现，并从 runtime context 获取 canonical identity。
- navigation / view / model / security / localization authoring 分别拥有静态路由、菜单、模型、权限和文案事实。
- 前端 TypeScript behavior binding 只把稳定 key 绑定到 page、renderer 或 action implementation。

C#、TypeScript 和子清单不得重新声明 Capsule 已拥有的 identity，也不得与 authoring metadata 平行维护 route、menu 或 permission。

## Runtime contract

`CanonicalModulePlanCompiler` 把候选 Capsule、metadata 与 behavior references 编译为一份确定的 Canonical Module Plan。Host 与 Shell 只消费该计划生成的活动投影：

- backend load view；
- `GET /plugins/active-module-runtime.json`；
- frontend / metadata / localization compatibility projections；
- permission 与 model indexes。

所有投影由同一启动期计划生成并记录 `snapshotId` / `planHash`。冲突或引用缺失必须在该次启动中拒绝整份 candidate；不得依赖加载顺序覆盖、warning 后跳过或部分注册。当前不支持进程存活期间改变插件集合，因此跨请求投影 identity 核对、运行中原子切换与回滚不属于本契约；见 ADR-0032。

## Guardrails

- Host 只组合、装载和提供宿主级横切能力，不吸收模块私有业务规则。
- Shell 不维护业务模块 ID 列表；前端实现由 Active Module Runtime Catalog 选择并一次提交。
- 共享声明通过 Kernel / Contracts / SDK 的稳定契约协作，不复制进模块。
- migration/import 逻辑留在显式 persistence migration seam，不进入 Host 组合。
- compatibility adapter 必须显式标记并有退出条件，不得成为新的平行真源。
- Host 运行期间的插件安装、升级、启用、停用、卸载和回滚不属于受支持 module contract；只读 inventory 只观察停服部署结果。
