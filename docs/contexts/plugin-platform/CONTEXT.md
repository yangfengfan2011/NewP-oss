# Plugin Platform

Plugin Platform 上下文管理仓库外插件包从离线部署意图到启动期激活和运行时投影的生命周期。它不拥有插件的业务领域，也不把 Host、Shell 或持久化实现细节暴露给普通插件作者；当前不提供 Host 运行期间的插件变更能力。

组件组合、平台必需集合与独立生命周期的已确认目标见 [ADR-0055](../../adr/0055-component-composition-separates-model-identity-and-deployment.md)，尚待 Issue #258 实施；下述相关术语不表示当前源码已完成该目标。

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
