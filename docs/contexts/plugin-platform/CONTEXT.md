# Plugin Platform

Plugin Platform 上下文管理原生组件发行与仓库外插件包从离线部署意图到启动期激活和运行时投影的生命周期。它不拥有组件的业务领域，也不把 Host、Shell 或持久化实现细节暴露给普通插件作者；当前不提供 Host 运行期间的插件变更能力。

组件组合、平台必需集合与独立生命周期见 [ADR-0055](../../adr/0055-component-composition-separates-model-identity-and-deployment.md)。通用组合、必需基础集合、全局模型身份、可选祖先屏蔽、停用保留和独立 owner 交错迁移已落地。激活 Gate/Session 拥有可信候选归档至最终部署确认的顺序，Host 只选择适配器、组合服务、Build 并运行。统一部署适配器同时拥有恢复编译与 active/LKG 的确认读取策略；显式无部署确认分支保留既有语义，SQL Server 未确认 raw active/LKG 不可作为活动事实，旧已确认 LKG 保留。pending 的实际历史恢复继续由持久化内核裁决。当前消费契约见 [Module Contract](../../architecture/module-contract.md)。

## Language

**插件包（Plugin Package）**：
仓库外插件的唯一可安装交付物，包含版本化根清单、完整性清单以及该版本的后端、前端、元数据、权限、模型和迁移贡献。
_Avoid_: 源码目录、程序集目录、前端压缩包

**期望状态（Desired State）**：
运维方在 Host 停止期间声明插件下一次成功激活后应处于的版本和启用状态。改变期望状态必须通过后续完整重启生效。
_Avoid_: 当前状态、已加载状态、即时热启用

**活动状态（Active State）**：
当前 Host 与 Shell 共同消费、并已通过启动期组合和迁移闸门的插件版本及启用状态。
_Avoid_: 安装状态、期望状态、磁盘上最新版本

**活动插件快照（Active Plugin Snapshot）**：
一次成功激活后原子提交的所有活动插件及其贡献集合，是后端装载、前端清单、元数据和权限投影的共同运行时事实源。
_Avoid_: 前端清单、扫描结果、已安装包列表

**贡献计划（Contribution Plan）**：
由插件声明编译得到的、可校验且确定的内部中间表示，用于排序和组合 API、页面、菜单、权限、元数据、模型与迁移；它不是首版普通插件作者直接编写的公共协议。
_Avoid_: 任意启动代码、开放 Adapter 图、运行时回调脚本

**Standard Plugin Blueprint**：
面向标准数据插件作者的声明式入口；平台从实体和字段声明派生标准 CRUD、页面、菜单、权限、元数据、模型与迁移。复杂领域插件必须使用后续明确的高级接口，不扩张该 Blueprint。
_Avoid_: 通用工作流 DSL、Host 配置、生成代码真源

**模块胶囊（Module Capsule）**：
一个模块的可构建 authoring 边界；根声明唯一拥有身份、版本和依赖，metadata 与行为代码各自拥有局部事实，并由同一编译器连接。它不要求把所有事实写进一个大文件。
_Avoid_: 源码目录约定、运行时插件目录、巨型 manifest

**组合组件（Composite Component）**：
把多个保留独立身份、版本和生命周期的组件组织为一个产品整体的组件。组合关系不重新拥有成员组件的模型、数据或迁移流。
_Avoid_: 合并模块、共享模型 owner、父组件拥有子组件数据

**组件源码根（Component Source Root）**：
可整体移动的组件前端与声明 authoring 根，包含自身的声明、metadata、前端实现、资源和迁移清单；后端实现与迁移工程保持原位，通过稳定组件身份关联其构建产物。嵌套成员有各自的源码根，目录包含不合并组件身份、资产归属或数据归属。
_Avoid_: namespace 前缀、模型 owner、安装版本目录

**聚合发行包（Component Bundle）**：
一次性交付一个组合组件及其成员组件产物的发行单位，以整包内容摘要标识交付物，不另维护人工包版本。成员保留各自版本并可独立选版升级，包内交付组合不替代实际期望状态。
_Avoid_: 合并组件、统一成员版本、父组件拥有成员数据

组件源码根、位置无关构建和聚合 build/pack/inspect 已实现；检查结果是内容一致的静态候选，不授予原生信任。真实 InforCenter/Part 首片已通过本机专用入口接纳库存，在未预装实现的固定 Host/Shell 中经显式选版、完整启动激活及 URL 前端消费，保持既有 SQL 数据、dbo owner 和迁移历史。原生来源由安装根 receipt 与核对后的固定快照签发，不由 SourceKind/profile 或 checksum 签发，也不授予平台必需角色。

普通外部开关只影响普通外部候选与部署入口，原生库存和选择共用安装根的一份 desired/active 状态。整包选版先输出完整版本差异，全部显式裁决后原子合并；启用根和新成员，保留已有成员自身启停及包外选择。未选库存默认项不成为运维选择，成员可独立升版且包遗漏不自动卸载。旧关闭态状态根有冲突时失败关闭并定位两份选择；显式归并后旧文件保留原字节、退出选择入口，恢复材料保持。安装只追加库存。整批校验与暂存后，`native-install.pending` 以原库存字节和目录/receipt所有权记录持久恢复事实；目录、receipt及多成员库存完成后才移除journal。Host与正式离线入口在安装根独占租约内、读取库存前恢复完整旧集合，恢复中断可重复执行；损坏或不可恢复时保留journal与材料并失败关闭，不消费半集合，也不修改desired、active、Persistence pending或恢复归档。主仓三成员归组及后续扩展和退出仍未完成。边界见 [ADR-0056](../../adr/0056-movable-component-roots-and-aggregate-delivery.md)，当前开发入口见[内建模块开发指南](../../developer/componentized-development.md#原生发行的离线接纳与独立消费)。

**成员组件（Member Component）**：
被归入组合组件、仍保留自身身份、版本和生命周期的组件。成员资格与组件之间实际消费能力的依赖关系是两个不同概念。
_Avoid_: 内部目录、父组件的私有实现、依赖项等于成员

**有效启用（Effective Enablement）**：
组件自身选择启用且全部祖先组合组件均启用时，组件具备进入下一活动快照的资格。父级屏蔽不改写成员自己的启用选择，也不替代实际依赖和完整候选校验。
_Avoid_: 继承父级期望状态、父级启用强制打开全部成员、组合关系等于依赖关系

**Canonical Module Plan**：
Module Capsule 经规范化、引用连接和冲突校验后得到的确定内部计划；Host、Shell、metadata、localization 和 Authorization 的投影都从它生成。
_Avoid_: 根清单、FrontendModule、运行时 manifest

**行为绑定（Behavior Binding）**：
C# 或 TypeScript 将稳定行为 key 绑定到服务、endpoint 或组件实现的声明；它不重新声明 module identity、路由、菜单或权限。
_Avoid_: 模块声明、页面路由、运行时注册副作用

**活动模块运行时目录（Active Module Runtime Catalog）**：
从活动插件快照的 Canonical Module Plan 原子生成、带统一 snapshot ID 的 Host 与 Shell 消费入口。其引用的各物理投影不是独立真源。
_Avoid_: frontend manifest、metadata manifest、workspace glob

**平台组合（Platform Composition）**：
由平台发布物指定、其自身及成员后代共同构成必需基础能力集合的组件组合。业务组件可依赖该集合始终可用；组合不重新拥有成员的身份、模型、数据或迁移流。
_Avoid_: Shell 内建模块数组、host-managed 特判、模块源码清单

**必需组件（Required Component）**：
平台运行不可缺失或停用的组件，也不能因祖先组合停用而被屏蔽。必需性由平台组合界定；随平台交付的内建组件并不因此自动成为必需组件。
_Avoid_: 内建即必需、组件自行宣布不可禁用、静默改写启用选择

**投影溯源（Projection Provenance）**：
记录每个运行时事实来自哪个 authoring owner、source path 和 compiler contract，用于解释和诊断，不参与运行时覆盖优先级。
_Avoid_: 加载顺序、后写覆盖、调试日志

**Platform API Release**：
一次原子发布、共享同一 SemVer 和 release digest 的外部插件 Contracts、后端 SDK、前端 SDK 与 CLI 工具集合。它描述插件作者 interface，与 Host 产品版本分离。
_Avoid_: HostVersion、某个单独 NuGet 版本、仓库 commit

**Standard Data Profile**：
首个公开插件 profile（`standard-data.v1`）；只允许 Standard Plugin Blueprint、由其派生的后端/前端投影，以及 #6 规定的 EF migration bundle。它不开放任意 Controller、DI callback、React 页面或模块生命周期代码。
_Avoid_: 外部插件模板、代码插件、通用 capability profile

**Authoring SDK**：
外仓在 restore/build/test/pack 阶段精确版本化使用的验证器、builder、analyzer 和生成工具；它不得成为 Host 运行时依赖，也不得把 Kernel、Shell 或 provider 实现传递给插件。
_Avoid_: runtime SDK、项目路径引用、Host library

**Release Attestation**：
pack 工具根据实际 resolved lockfiles 和 Platform API release manifest 生成的构建证明，记录精确包版本、release digest、profile 与工具链；作者不能手工声明一个未经构建证实的兼容版本。
_Avoid_: package version、checksum manifest、人工兼容说明

**兼容范围（Compatibility Range）**：
插件包声明可接受的 Platform API SemVer 区间。首个 profile 的下界等于 built-with release，上界为下一 major；Host 产品版本不参与该判断。
_Avoid_: hostVersion min/max、npm dependency range、模块依赖范围

**代码型 Profile（Code-bearing Profile）**：
未来允许外部程序集或浏览器脚本绑定自定义行为的独立插件 profile。它必须另行决定进程内依赖隔离、受控 DI、前端运行时共享和失败域，不能把 Standard Data Profile 悄悄扩张成任意代码入口。
_Avoid_: Standard Data Profile、EF migration bundle、内建模块兼容 adapter

**契约弃用（Contract Deprecation）**：
在同一 Platform API major 内保留旧成员并提供稳定 warning、替代项和 removal major 的演进状态；弃用不等于删除、unpublish 或运行时静默忽略。
_Avoid_: breaking change、包删除、兼容失败

**激活（Activation）**：
Host 启动期间对期望插件集合完成整体验证、组合、向前迁移、权限同步和运行时投影，并原子提交新的活动插件快照。
_Avoid_: 安装、扫描、前端清单重建

**禁用（Disablement）**：
把插件从下一活动快照中移除，使其 API、页面、菜单和权限入口不再可用；默认保留插件拥有的数据、表和迁移账本。
_Avoid_: 卸载、删除数据、回滚迁移

**离线插件部署（Offline Plugin Deployment）**：
Host 停止期间对插件包和期望状态进行的受控变更，必须由随后一次完整 Host / Shell 重启验证并生效。
_Avoid_: 热安装、运行时启用、在线回滚
