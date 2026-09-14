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

## 功能位置授权

已切换的标准入口在 navigation 声明 `featureAuthorization: true`。`StandardFeatureMetadataCompiler` 展开真实 View 和 PageRef，`FeatureCatalogCompiler` 与 Capsule 的自定义功能声明合并为同一目录。导航、Tab、动作是授权点；页面和静态类型展示组不是授权点。同一 View 在不同分支复用时分别生成位置，局部动作 Key 可以重复。

View 的 `resourceKey` 指定稳定资源；`lookupBindings` 在该 View 的资源范围内把模型字段或查询 override 所用 provider 绑定到只读 behavior。专用动作通过 `featureActions` 声明 `key`、`titleKey`、`behaviorKey` 及可选 `readBehaviors`。标准 `pageActions` / `rowActions` 也可声明 `readBehaviors`，补充动作输入所需的只读行为；它们与资源读取和实际表单 Lookup 一同随发起动作授权。这些绑定描述实际消费能力，不向管理员增加接口权限清单。

完整位置由声明引用及其祖先、静态节点类型和动作所在位置编译产生，不能使用 URL、文案、数组序号或记录 ID。导航的 `featureOwner` 为重定向别名和独立详情指定已有入口引用；别名不创建重复授权点。`ReferenceKey`、`StaticScope`、`ActionScope`、`StaticScopeTitle` 和 `Container` 是标准适配器的派生事实，不能据此推断 Capsule 自定义声明已经支持这些输入字段。

纯导航快捷动作以 `navigationTargetKey` 引用已有导航，沿用现有 handler 与 navigation-result 跳转；按目标导航的位置资格呈现，不生成独立业务动作点。目标必须是采用位置授权的真实导航，缺失或无效引用拒绝候选；不能借用当前资源的查看或保存资格。

Host 在开放请求前核对目录与真实端点的模块归属及执行绑定。`RequireFeature` 只允许当前位置声明的行为；附加读取必须绑定 GET/HEAD 端点，纯容器不获得后代读取。缺失、冲突或无效绑定拒绝整份候选。运行时投影提供编译后的 position，前后端不得另外拼装授权身份。

当前 Organization 标准路径，以及 Authorization 的角色读取、创建、编辑、账号角色分配和工作台消费位置授权。角色页面未声明删除动作，遗留删除 HTTP 入口明确拒绝；旧角色 Permission 读取/保存接口暂保留原 Permission 与 LocalAdmin 准入，随旧模型退出。其他未切换消费者暂保留原 Permission 路径。已切换端点不以新旧任一授权通过作为放行条件。其余消费者切换与旧目录一次性退出由 [Issue #200](https://github.com/yangfengfan2011/NewP/issues/200) 的后续 child 完成。

## Guardrails

- Host 只组合、装载和提供宿主级横切能力，不吸收模块私有业务规则。
- Shell 不维护业务模块 ID 列表；前端实现由 Active Module Runtime Catalog 选择并一次提交。
- 共享声明通过 Kernel / Contracts / SDK 的稳定契约协作，不复制进模块。
- migration/import 逻辑留在显式 persistence migration seam，不进入 Host 组合。
- compatibility adapter 必须显式标记并有退出条件，不得成为新的平行真源。
- Host 运行期间的插件安装、升级、启用、停用、卸载和回滚不属于受支持 module contract；只读 inventory 只观察停服部署结果。
