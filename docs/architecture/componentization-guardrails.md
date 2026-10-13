# 组件化护栏

## 目标与适用范围

这份文档把组件化边界收紧为新增模块和平台能力默认必须遵守的实现契约，适用于：

- 新增或调整 Module Capsule、模块 metadata 与 behavior binding；
- 新增后端模块、前端模块以及 Host / Shell 受控接入；
- 新增公共 Contracts、SDK、Kernel 或 `platform-*` 能力；
- 新增持久化迁移、数据导入或兼容适配。

本文不要求一次性重构所有历史模块；但新代码不得继续扩大旧的平行真源或宿主特判。

## 唯一事实源与运行时投影

每个内建模块的可提交 authoring 真源位于：

- `<组件源码根>/newp.plugin.json`：唯一拥有 module id、显示名、版本、宿主兼容范围、依赖和 backend / frontend behavior key；
- `<组件源码根>/metadata/**`：拥有导航、路由、视图、模型、权限引用和本地化等静态声明；
- `server/modules/<Group>/<ModuleId>/**`：后端行为实现；
- `<组件源码根>/**`：前端行为实现。

初始声明与 metadata 目录采用现有小写连字符 slug；组件身份仍来自 Capsule。组件源码根允许按组合 JSON 嵌套，最近 Capsule 根界定目录 owner；发现与构建契约见[内建模块开发指南](../developer/componentized-development.md#可移动组件源码根与构建)。仅有声明或 metadata 的模块保留实际贡献，不补建空前端包。前后端成员分别归入 `example/platform/infor-center` 与 `Example/Platform/Infor-Center`；后端工程按组件身份递归定位，迁移工程随 owner 目录移动。Host 发布输出仍是 `modules/<ModuleId>/`。尚无 Capsule 的历史 metadata 根沿用既有目录命名约定恢复 owner；该兼容不授予组件身份或发行资格。

C# 和 TypeScript 只把稳定 behavior key 绑定到实现，不得重新声明 Capsule 已拥有的身份，也不得平行维护路由、菜单或权限。`ModuleCapsuleBindingBase` 与 `FrontendBehaviorBindings` 是当前标准绑定入口。

Host 编译出的 Canonical Module Plan、`active-module-runtime.json`、backend load view、权限索引和其他 runtime 目录都只是当前生效投影。投影不得反向成为 authoring 真源，也不得手工维护。

Model 固定字段目录与服务端所有权由后端 modeling 统一派生；runtime `model.fields` 是 List、Detail 和 Form 的完整解析后字段目录。普通 authoring Model 在 Prepare 与 Stage 使用同一完整集合投影，standard-data Model 在 Stage 内存生成后、写入 staging 前调用同一 projector。Shell 不得维护或合并固定字段名单，`persistence.models` 子集和 authoring 回写也不能替代 runtime Model 集合，详见 [ADR-0045](../adr/0045-server-owned-fixed-fields-use-runtime-form-projection.md)。

当前插件集合只在 Host 启动期编译和装载。插件包与期望状态的变更必须在 Host 停止期间完成；运行时插件运维写入口、热切换和热回滚不属于受支持边界，详见 ADR-0032。

冲突、引用缺失或 behavior key 不一致时必须拒绝整份 candidate；不得依赖加载顺序覆盖、warning 后跳过或部分提交。

## 公共能力的放置规则

### Contracts

放稳定、跨边界的数据形状、协议和最小接口，例如插件契约、DTO 或可序列化描述。Contracts 不负责装载、状态切换、持久化实现或模块私有业务。

### SDK

放面向模块开发者的薄适配、基类和注册帮助器，例如 behavior binding 基类。SDK 降低接入成本，但不拥有模块身份、运行时目录或冲突裁决。

### Kernel

放必须全局一致的机制：Capsule 读取、静态校验、依赖排序、冲突处理、Canonical Module Plan 编译、原子切换与回滚。Kernel 不吸收具体模块的业务规则。

### `platform-*`

放被多个模块复用、具有独立边界的基础能力和基础设施实现，例如授权、CRUD、建模和持久化组合。平台能力通过稳定抽象暴露，不根据业务模块 ID 特判。

### 模块内部

以下内容默认留在所属模块：

- 模块私有 DTO、应用服务、领域服务和数据访问实现；
- 模块私有页面、组件、页面状态和 API client；
- 模块私有模型、映射、查询、命令和校验规则；
- 只服务于该模块的迁移、导入映射和兼容代码。

一个模块不得直接依赖另一个模块的具体基础设施实现。确需跨模块协作时，先提炼稳定契约，并由能力拥有者实现。

## Host / Shell 护栏

### Host 只负责

- 启动、依赖注入组合和宿主级配置；
- 认证接入、全局中间件和宿主级横切能力；
- 模块候选读取、计划编译、装载和运行时投影；
- 受控的内建模块开发期工程发现与按身份生成的 Capsule 输出链接。

Host 不承载模块私有业务规则、数据访问、临时迁移或 module id 分支。

### Shell 只负责

- 布局、全局 Provider 和认证上下文；
- 消费 `active-module-runtime.json`；
- 菜单、路由和实现绑定的聚合；
- 在完整候选通过后原子提交模块注册。

这里的完整候选只指本次 Host 启动公开的静态集合。Shell retry 不得成为运行时插件安装、升级、启用、停用或跨快照切换入口。

Shell 不维护业务模块清单，不通过源码目录推断活动模块，也不为单个模块添加静态 import 列表或 module id 特判。

当现有受控入口不足时，应先通过设计门禁决定新增契约或扩展点，而不是直接修改 Host / Shell 内部结构。

## 后端模块护栏

- `*Module.cs` 只声明稳定 backend behavior key 并注册服务。
- Capsule 身份通过嵌入的 `newp.plugin.json` 适配到现有模块接口。
- Controller 保持轻薄，授权入口使用平台授权契约，业务逻辑进入应用服务或领域服务。
- 对外返回稳定 DTO；不要为重复 Capsule 身份而在响应中增加 `ModuleId`。
- 模块项目只引用实际需要的 SDK、Contracts 与 `platform-*` 抽象，不引用 Host 内部实现。

## 前端模块护栏

- `src/index.ts` 只导出 `FrontendBehaviorBindings`。
- `behaviorKey` 必须与根 Capsule 一致。
- 页面、renderer 或 action 通过稳定 key 注册；metadata 中的 `targetKey` 必须存在对应 binding。
- 路由、菜单、权限和模块身份只写在 Capsule / metadata，不在 TypeScript 中复制。
- 模块不得引用 `frontend/packages/shell/src/*`；共享 UI 放在 `frontend/packages/ui`。
- 状态优先局部、显式；兼容入口要隔离并记录退出条件。

## 持久化、迁移与导入

- 模型 owner 和 migration bundle 由模块 Capsule / metadata 显式声明。
- provider 选择、迁移计划编译、锁、执行与失败恢复属于平台持久化内核。
- 模块只提供自己的迁移产物或导入映射，不直接控制 Host 启动流程。
- Required 字段的投影、写入顺序、迁移状态与门禁属于平台机制；模块拥有字段语义、值来源和旧行回填值。平台当前不提供数据库永久 `DEFAULT`，也不为回填建立独立 CLI 或通用数据转换 interface。
- 数据导入不是 schema migration；两者使用不同入口、状态和回滚语义。
- 禁止在 DI 注册、Controller 构造或模块装载副作用中隐式迁移或导入。

## 模板与脚手架契约

`server/templates/backend-module-template` 与 `frontend/templates/frontend-module-template` 必须始终保持可独立构建，并体现当前标准边界。

内建模块脚手架必须生成：

- `<组件源码根>/newp.plugin.json` 与最小 metadata；
- 继承 `ModuleCapsuleBindingBase` 的后端 behavior binding；
- `FrontendBehaviorBindings` 前端实现；
- 通用构建入口可发现的归组后端工程与组件源码根；父目录有 Capsule 时同步登记其 `composition.members`。

脚手架不得生成新的 `apps/host-api/frontend/modules/<module-slug>/plugin.json`、`legacy-module` 前端声明或 Shell 内部注册表。该 Host 目录只允许承载尚未完成 Capsule cutover 的历史兼容清单。

内建脚手架不再编辑 HostApi 接线；`-SkipIntegration` 保留命令兼容，仍应生成完整 authoring Capsule 和两侧 behavior binding。外部插件模式不修改 Host 或 Shell。

## 设计门禁与失败信号

新增公共 Contracts / SDK、Host / Shell 受控入口、metadata 协议、持久化语义、安全边界或难以回退的装载机制前，必须有已确认的设计工件。

Detail View 的静态结构错误仍在 metadata 解析阶段失败；单次详情响应中的普通字段缺失只由共享详情呈现 module 局部降级，不得重新引入响应字段白名单、Model 推断或模块专属分支，详见 ADR-0033。

出现以下任一信号时停止实现并回到边界设计：

- 同一 module id、路由、菜单或权限需要在两个位置同步修改；
- 新模块必须修改 Shell 内部列表或 Host module id 分支才能出现；
- 模块需要依赖其他模块的 EF 实现、私有服务或内部目录；
- runtime 投影需要人工编辑才能生效；
- Capsule、metadata 与 behavior binding 无法通过 Canonical Module Plan 编译。

## 实施检查清单

1. 是否只有根 Capsule 拥有模块身份、版本、依赖和 behavior key？
2. metadata 的每个 `targetKey` 和权限引用是否都有唯一 owner？
3. C# / TypeScript 是否只绑定行为，没有复制身份、路由、菜单和权限？
4. 新的共享能力是否放在正确的 Contracts、SDK、Kernel 或 `platform-*` 边界？
5. Host / Shell 改动是否仅限已确认的受控入口？
6. 持久化迁移和数据导入是否具有显式入口、失败信号与回滚思路？
7. 模板、生成结果、Canonical Module Plan 和受影响 Host / Shell smoke 是否都有可复跑验证？
