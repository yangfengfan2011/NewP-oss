# 内建模块开发指南

本文是新增仓内模块的唯一开发入口。模板和脚手架是当前示例；`ModuleTemplateSample` 只保留为旧契约兼容样本。

## 所有权

一个模块由三个可独立核对的所有者组成：

1. `<组件源码根>/newp.plugin.json` 唯一拥有 module id、显示名、版本、依赖和 behavior key；源码根位于 `frontend/modules` 内，可按组合关系嵌套。
2. `<组件源码根>/metadata/**` 拥有路由、菜单、权限引用、模型与本地化等静态事实。
3. 后端和前端代码只把稳定 key 绑定到服务、endpoint、页面、renderer 或 action 实现。

Host、Shell、C# 与 TypeScript 不再维护平行的模块身份和静态声明。

## 推荐流程

### 1. 先确认边界

新模块、公共 Contracts/SDK、Host/Shell 入口、持久化或安全语义变化必须先有已确认的 issue/spec，并核对[组件化护栏](../architecture/componentization-guardrails.md)。

### 2. 使用脚手架

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

生成结果包括：

```text
frontend/modules/example/example-module/     Capsule、metadata 与前端 binding
server/modules/Example/ExampleModule/        后端 binding 与实现
```

`-Group` 可选 `Example`（默认）、`Platform`、`Infor-Center`；前端分组目录使用对应的小写名称。内建模块由通用构建入口发现，不再编辑 HostApi 或 Shell 接线。父目录已有 Capsule 时，脚手架将新模块登记到该 Capsule 的 `composition.members`；普通分组目录不生成额外身份。`-SkipIntegration` 保留命令兼容，不省略 Capsule、两侧 binding 或成员登记。

### 3. 实现局部能力

- Controller 只处理协议、认证授权入口和应用服务调用。
- 稳定跨模块数据形状放入模块 Contracts；模块私有实现留在模块内。
- 前端 `src/index.ts` 只导出 `FrontendBehaviorBindings`。
- 模型与 migration 按[持久化开发流程](persistence-development-workflow.md)处理。
- 共享 UI 进入 `frontend/packages/ui`，普通模块不引用 Shell 内部源码。

### 4. 验证启动期静态集合

先执行模块级构建和定向测试，再构建 Host 与前端模块。接入完成必须证明：

- Capsule、metadata 和 behavior references 能编译为同一 Canonical Module Plan；
- 冲突或缺失引用会拒绝整份候选；
- 所有 runtime 投影都来自同一启动期计划；
- 前端模块全部装载成功后才一次注册本次启动的静态集合；
- 任务涉及的权限、持久化或启动 smoke 有可复跑验证。

插件变更只允许在 Host 停止期间通过离线部署工具完成，然后重启 Host / Shell。运行中的安装、升级、启用、停用、卸载和回滚不是开发或验收入口；只读 inventory 也不代表这些写能力可用。详见 ADR-0032。

仓库级验证使用 `scripts/verify.ps1 -Quick`；全量验证去掉 `-Quick`。

## 可移动组件源码根与构建

前后端模块目录只保留三个顶层分组，成员实现与迁移工程随所属分组存放：

| 分组 | 前端目录 | 后端目录 | 成员 |
| --- | --- | --- | --- |
| Example | `frontend/modules/example/` | `server/modules/Example/` | Demo、ModuleTemplateSample 与演示／冒烟资产 |
| Platform | `frontend/modules/platform/` | `server/modules/Platform/` | Identity、Authentication、Authorization、Organization、Audit、ConfigCenter、MetadataConfigCenter、Files、PluginOps 的实际贡献 |
| Infor-Center | `frontend/modules/infor-center/` | `server/modules/Infor-Center/` | Workspace、Part、Document |

Example 自身的实现仍在分组根内；纯分组或仅有声明／metadata 的组件不补造工程。目录归组不改变组件 ID、程序集名、npm 包名、behavior key、数据 owner 或迁移历史。Platform 的平台角色与组合声明仍由现有 Host 清单拥有，普通 `platform` 分组目录不新增 Capsule。

组件身份来自 `newp.plugin.json`，`composition.members` 是唯一成员关系事实源。发现器按最近 Capsule 根核对嵌套 owner，不要求中间目录叫 `members`；开始嵌套后，父组件的全部直属成员必须位于该源码边界内。普通中间目录只归组源码；以组合根发起的完整集合检查要求其声明后代都实际嵌套。缺失、重复、自含、多父、环或声明与目录不一致直接拒绝。

`ComponentSourceCatalog` 复用正式 Canonical 编译器校验身份与组合图；模块资源嵌入、Host metadata/迁移资源链接、模型读取、DTO 和前端入口都消费按身份发现的当前根。MSBuild 通过项目依赖构建 `server/tools/component-source-build`，在短生命周期 CLI 中生成本轮 `obj` 映射，再用内建任务读取。映射只表示当前构建的路径，不是可提交的第二份组件或成员表。项目引用显式传递调用方配置，构建工具路径来自真实 `GetTargetPath` 输出。Host 发布目录保持 `modules/<ModuleId>`；父组件自有文件枚举在独立成员根处停止。

Host 发现内建后端工程时，以候选 `.csproj` 的直接目录识别 owner；`Contracts` 和 `Migrations.<provider>` 是当前辅助工程目录约定，归属其上一级组件 owner，其他工程使用直接目录的 owner。`NewpExcludedModuleIds` 仅匹配显式组件 ID，不按祖先目录通配：裁剪父组件不会连带裁剪其目录下独立的子组件；需要裁剪完整集合时显式列出各组件 ID。

核对当前组件和完整归组集合：

```powershell
dotnet run --project server/tools/component-source-build/ComponentSource.Build.csproj -c Release -- describe . InforCenter
dotnet build apps/host-api/HostApi.csproj -c Release -p:NewpComponentRoot=InforCenter
```

主仓 Workspace、Part、Document 位于 `frontend/modules/infor-center/{workspace,part,document}`，后端与迁移位于 `server/modules/Infor-Center/{Workspace,Part,Document}`；上述命令消费完整嵌套集合。Catalog 在 `server/modules` 内按独立工程所在的 owner 目录递归定位后端，再核对声明的程序集；缺失或重复 owner 直接拒绝。迁移目录时同步修复实际工程引用和 solution 路径，Host/Shell 继续使用通用发现。父后端工程排除嵌套独立工程的源码和资源，避免将子模块编译进父程序集。前端 workspace 使用递归模块发现，Vite 与 `build-frontend-plugin.mjs` 通过同一 Catalog CLI 获取稳定 entry key 对应的真实源码入口；新增或调整测试配置时，从 workspace 根解析共享包，不按组件嵌套深度维护相对路径。

源码根和位置无关构建已落地。回退需同时恢复通用构建入口与工程资源配置，已实际归组的目录按其 JSON 关系还原；不改写模型、manifest、已提交迁移或数据库历史。

## 聚合发行构建与静态核验

正式入口 `server/tools/component-bundle` 按同一 `ComponentSourceCatalog` 取得完整归组，构建按身份定位的 owner 工程与现有前端 plugin builder，再从实际产物生成索引。纯组合可以只拥有声明，不补造后端、前端、模型或迁移入口。输出目录和 ZIP 必须是新的目标；所有命令均不写安装库存、desired、active 或 authoring。

```powershell
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release -- build . InforCenter <新的暂存目录>
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release -- pack <暂存目录> <新的ZIP路径>
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release -- inspect <暂存目录或ZIP>
```

`build` 要求声明与成员目录完整对应；当前 InforCenter 正式集合包含纯组合父组件与三个独立成员。后端 Release 参数只在正式发行入口排除调试路径与仓库提交号，不改变开发调试配置；产物路径来自 MSBuild `TargetPath`，目标框架及实际共享依赖必须匹配受控平台运行时发行。私有程序集按真实引用闭包从 owner 构建输出收集，受控平台依赖与 CLR 实际共享框架由目标发行提供；未知或版本不一致的引用在消费前拒绝。迁移沿用原项目的确定性设置，打包不执行 finalize。

包根包含 `component-bundle.json`、`platform-runtime-release.json` 与 `modules/<ModuleId>/`。每个组件根保存原始 `authoring/newp.plugin.json`、派生 `plugin.json`、自身 metadata/resources、实际后端 DLL、前端完整产物，以及声明的 migration manifest/DLL 与已发布 canonical source。原 Capsule 是唯一声明真源；派生 transport 只把后端映射为 `backend/<AssemblyName>.dll`、前端映射为 `frontend/entry.js`，其他声明严格保持。检查器逐项比对该映射及后端嵌入的 Capsule，不追随原声明的历史 `../../` 程序集路径。

成员索引记录精确版本、自有内容摘要、固定相对根和每个文件的 SHA-256。组件摘要从按相对路径排序的自有资产索引计算，排除成员；整包摘要从平台发行身份及全部组件索引计算，不增加人工包版本。ZIP 使用固定条目顺序和时间戳。同 ID 同版本相同内容可复用，不同内容通过 `ComponentBundleInspector.RequireReusable` 拒绝；库存 owner 在提交前消费此规则，检查器不维护库存。

平台发行方维护 `server/plugin-platform/ComponentBundles/platform-runtime-release.json`，它拥有公共行为/持久化契约、.NET 目标、共享程序集和前端依赖的精确发行版本，并固定既有存储类型映射摘要。该受控声明嵌入平台代码，业务 HEAD、组件根位置和父组件版本均不参与。正式构建核对真实 MSBuild 输出、CLR 引用版本、现有存储输入及 Node 实际解析的前端依赖；前端 builder 从 Rollup 输出生成 `frontend-artifacts.json`，记录全部 chunk/资源引用和共享运行时引用。共享契约或依赖发行变化时由平台发行方更新此声明，不能让业务包自行增加 Host 支持的发行身份。

`inspect` 与 `ExtractVerified` 在返回候选前检查整个资产集合、相对路径、大小写碰撞、链接、所有摘要、平台身份、声明映射和前端引用闭包，并通过正式 Capsule reader/Canonical 编译器读取候选。后端检查只读取 PE/CLR metadata，不加载包内代码；模型投影、manifest 前缀、bundleChecksum 和 canonical source 复用现有持久化完整性规则。检查通过及 checksum 只证明资产一致，不授予可信原生身份、平台必需角色或执行许可。

回退可撤销构建工具与发行协议变更，删除未安装的暂存产物；不能借此改写已发布 manifest 或迁移历史，也不能把源码/ZIP 回退当作数据库回退。

## 原生发行的离线接纳与独立消费

持有目标安装根部署权限的操作方在 Host 停止时使用已发布 Host 的专用入口。安装接受完整发行目录或 ZIP，严格匹配受控平台运行时发行；路径、资产集合、摘要、声明映射及同 ID/版本的内容复用检查全部在库存提交前完成。平台发行物指定的平台组合角色不能被原生包替换；普通 `install`、包内 SourceKind/profile 和静态检查结果不能授予原生信任。

```powershell
dotnet <目标Host目录>/HostApi.dll plugin-deploy install-native --package <发行目录或ZIP>
dotnet <目标Host目录>/HostApi.dll plugin-deploy enable-bundle --package <同一发行目录或ZIP>
```

命令使用相同目标配置和 `Platform:PluginInstallRoot`。`install-native` 只增加库存和安装根的接纳 receipt，不改变 desired、active 或数据库。`select-bundle --package <包>` 明确选定整包版本，新组件先停用；`enable-bundle` 明确启用根组件和未有运维选择的新成员，已有成员保留自身启用或停用选择。安装后的库存默认停用、Host 对未选库存的默认投影都不算运维选择；旧库存的显式 `IsDisabled` 仍算既有停用选择。所有包内版本必须已通过 `install-native` 接纳，选包不会代替安装或授信。

两个整包命令都先输出完整 `bundle-version-differences`。已选成员版本与包内版本不同，无论升版或降版，缺少任一裁决都以 `PLUGIN_BUNDLE_VERSION_CHOICE_REQUIRED` 拒绝且不写 desired。使用 `--decisions <JSON文件>` 提供数组，例如 `[{"moduleId":"Part","version":"1.1.0"}]`；每个差异恰好裁决一次，可选包内版本或其他已安装版本，包括保留独立升级版本。裁决只指定版本，不覆盖已有成员启停。批量操作合并所有无关组件，最后复用整份 desired 的原子保存；版本不存在、重复裁决或无关裁决同样拒绝。

成员可独立 `install-native --package <单成员发行包>`，再 `select --module-id Part --version 1.1.0`，该命令默认保留该成员自身启停。父组件不重发，重装旧整包也不覆盖独立选择；新包不再携带某成员，不自动卸载库存或删除其选择。既有 `select`、`enable`、`desired` 命令同样只保存完整选择。待选择完整后重启 Host / Shell，统一 Activation Gate/Session 校验完整候选并经 Persistence deployment 确认活动快照。选择期间尚未闭合的依赖不能启动，也不会部分激活、自动启用依赖或停用依赖者。

生命周期只有 `<PluginInstallRoot>/lifecycle/v2/state.json` 一份状态源。原生库存和选择不受 `ExternalPluginsEnabled` 影响；关闭第三方开关时保留普通外部组件的选择，但不消费其候选，也拒绝新的普通外部安装、选版和启用。旧 `.feature-gates/external-plugins-disabled/lifecycle/v2/state.json` 有不能由当前根覆盖的选择时，以 `PLUGIN_DESIRED_SOURCE_CONFLICT` 输出每个组件的当前与旧来源选择，Host 和其他离线操作失败关闭。

在停服租约内运行 `plugin-deploy reconcile-desired --file <裁决JSON>`，为每个来源差异明确提供 `moduleId`、已安装 `version` 和 `enabled`，例如 `[{"moduleId":"Part","version":"1.1.0","enabled":true}]`。全部差异恰好裁决一次才原子合并到当前根，保留其他组件及当前 active/LKG 指针。成功后旧文件改名为同目录 `state.reconciled-<随机标识>.json`，保留原字节及旧 snapshots、persistence 材料，但退出选择入口；没有差异的旧文件同样退出入口。若保存后、归档前中断，保留两个文件并在下次操作失败关闭或安全完成，不能删文件绕过冲突。旧文件不再随开关写入，也不导入其 active/LKG 指针或把它当成数据库恢复事实。

接纳 receipt 与重新核对的固定快照共同签发原生来源，后端从固定程序集字节和受控依赖装载；既有成员保持 dbo、模型 owner、物理映射与迁移摘要前缀。前端交付方式与信任分离，活动目录生成验证资产的 URL，物化器只公开 `frontend` 子树中的入口和懒加载资源，不公开后端、metadata 或 migration。Shell 的构建产物通过 import map 提供同一共享运行时实例；Shell 与组件 builder 共用受控发行声明并核对实际解析的共享依赖版本。

目标发行可通过通用身份参数排除待装实现，后续安装和消费不重建这些目标文件：

```powershell
dotnet publish apps/host-api/HostApi.csproj -c Release -t:Rebuild -p:NewpExcludedModuleIds=InforCenter%2cWorkspace%2cPart%2cDocument -o <新目标Host目录>
$env:NEWP_EXCLUDED_MODULE_IDS = 'InforCenter,Workspace,Part,Document'
pnpm --dir frontend --filter @icp-platform/shell exec vite build --outDir <新目标Shell目录>
Remove-Item Env:NEWP_EXCLUDED_MODULE_IDS
```

MSBuild 命令中的 `%2c` 转义逗号，避免将组件列表拆成多个属性。三成员正式包已贯通未预装它们的固定 Host/Shell：真实 Workspace、Part、Document API、旧业务行/内容 Link/迁移历史与 Workspace 页面消费连续。安装先完成整批校验与暂存，再以 `native-install.pending` 持久记录原库存字节及每个目录/receipt的先前所有权。提交期间进程中断，下一次正式离线命令或Host启动在同一安装根独占租约内、读取库存前恢复完整旧集合；恢复自身中断可重入。journal只在全部提交或全部恢复完成后移除；损坏、旧版仅摘要标记或无法恢复时保留journal与材料并以 `COMPONENT_BUNDLE_INSTALL_INCOMPLETE` 失败关闭，不能手工删除标记绕过。重复安装幂等，原选择、无关库存与Persistence pending/成功记录及恢复归档保持；文件恢复不连接数据库，也不代替数据库前沿恢复。整包选版和独立升级已沿固定 Host 重启及真实 Part API 验证；三成员归组、消费及显式集合退出/重装恢复已完成；真实部分迁移提交后叠加原生文件安装中断与恢复，SQL pending、成功事实、可信资产和历史保持，最终按真实数据库前沿恢复。父 #267 的完整审查与最终验证仍集中执行。

创建固定目标时，restore 和 publish 必须使用同一排除集合；上述命令完成 restore 并重建 Host，避免复用其他组合生成的 deps/MVC ApplicationPart 引用。目标固定后，安装、选版和重启只操作安装根；二进制身份比较以安装前后同一固定目标为准。

整包卸载由调用方明确列出组件 ID，不从 ZIP、组合关系或依赖自动推导集合。停服后运行 `dotnet <发行Host>/HostApi.dll plugin-deploy uninstall --file <组件ID数组JSON> --Platform:PluginInstallRoot <安装根>`；例如 `["InforCenter","Workspace","Part","Document"]`。已有 `uninstall --module-id <id>` 与集合入口使用相同预检；PowerShell wrapper 对应 `-Action uninstall -CandidatePath <JSON>` 或原 `-ModuleId <id>`，两者互斥。空集合、重复或不可用 ID 都拒绝整批。先检查平台必需保护，再用正式 Canonical 编译器验证删除后的完整候选；保留的包外组合引用即使停用仍阻止卸载，活动依赖也不能通过隐式停用、扩大或缩减集合解决。全部语义和路径检查通过后才删除指定代码/installed 库存并一次保存剩余 desired，随后完整重启才生效。

代码退出保留全局模型身份、已有表与业务行/Link、迁移历史、生命周期快照及必要可信持久化恢复资产。这些归档不成为 installed 库存或活动业务注册；需要恢复业务时重新接纳相容包、明确选版启用并重启。该卸载预检不承诺通用跨目录卸载事务；文件安装事务继续使用 `native-install.pending` 的既有恢复链，数据库部署 pending 与成功事实继续由 `PersistenceDeploymentStore` 按真实已提交前沿恢复。

回退应在停服租约内恢复相容的平台文件与明确选择；数据库前沿已变化时必须保留相容组件和恢复资产，不执行 Down、改写已提交历史或清除 Persistence pending。未完成原生文件事务也不能通过手工删除标记冒充恢复完成。

## Workspace 固定发行物验收输入

Workspace Gate 消费正式三成员包，不再用静态 dev 装配替代独立交付。普通验收使用 `accept.ps1 -Gate Workspace` 或 `accept.ps1 -Gate All`；完整验证使用 `verify.ps1 -AcceptanceGate Workspace`，也可在 `-AcceptanceGate` 中显式列出其他命名 Gate，列表包含 Workspace 时同样需要本节输入。先准备与本轮冻结候选对应的新包和平台发行物，再登记输入；开发候选可以 dirty。登记只记录 HEAD/tree/index/status、tracked/untracked 内容摘要及发行资产摘要，不授信、不产出另一套裁决。候选或固定目标变化时，在再次执行前重新准备受影响资产并登记，不能沿用返修前的旧发行物冒充最终候选。

旧数据由一份已有完整正式部署 Host 的真实 bootstrap/API 建立，随后同一个专用 `NewP_Test` catalog 切换到裁剪目标，正式 `install-native`、`enable-bundle` 后重启。baseline 必须包含三个成员的原实现；它与新目标用途不同。调用方必须提供该已有完整部署 Host 的绝对目录；缺少此历史输入的新环境不能用空库或新裁剪目标替代，也不能伪造已有数据连续性。SQL 管理连接沿用 Acceptance 的 `NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION`、`NEWP_DEV_SQLSERVER_CONNECTION_STRING` 和开发配置读取顺序，不把凭据写入交付输入或命令日志。

在仓库根、无其他 build/test/browser 占用时，逐条等待成功后继续。每次使用新的输出目录；Host publish 保留 restore 并 Rebuild，避免复用默认全模块的 obj/deps。下面全部完成后，安装、选版、重启期间不再重建目标：

```powershell
$repo = (Get-Location).Path
$deliveryRoot = Join-Path $repo ('.codex-temp/workspace-delivery/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $deliveryRoot | Out-Null
$baselineHost = (Resolve-Path -LiteralPath '<已有完整正式部署Host的绝对目录>').Path
$stage = Join-Path $deliveryRoot 'stage'
$package = Join-Path $deliveryRoot 'components.zip'
$hostRoot = Join-Path $deliveryRoot 'host'
$shellRoot = Join-Path $deliveryRoot 'shell'
$workspaceDeliveryInput = Join-Path $deliveryRoot 'input.json'

dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release -- build $repo InforCenter $stage
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release --no-build --no-restore -- pack $stage $package
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release --no-build --no-restore -- inspect $package
dotnet publish apps/host-api/HostApi.csproj -c Release -t:Rebuild -p:NewpExcludedModuleIds=InforCenter%2cWorkspace%2cPart%2cDocument -o $hostRoot
$env:NEWP_EXCLUDED_MODULE_IDS = 'InforCenter,Workspace,Part,Document'
try { pnpm --dir frontend --filter @icp-platform/shell exec vite build --outDir $shellRoot }
finally { Remove-Item Env:NEWP_EXCLUDED_MODULE_IDS -ErrorAction SilentlyContinue }
dotnet build tests/acceptance/Acceptance.Tests/Acceptance.Tests.csproj -c Release
dotnet run --project tests/acceptance/Acceptance.Runner/Acceptance.Runner.csproj -c Release --no-build --no-restore -- --workspace-delivery-input $workspaceDeliveryInput --repository-root $repo --baseline-host-root $baselineHost --host-root $hostRoot --shell-root $shellRoot --package $package
$env:NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT = $workspaceDeliveryInput
.\scripts\verify.ps1 -AcceptanceGate Workspace
```

输入登记会核对裁剪 Host 无三成员程序集、声明目录及 deps 引用，Shell 无对应构建期实现。正式 fixture 使用同一安装根、`ExternalPluginsEnabled=false`、`/plugins/runtime/assets` 与现有 production-shell-server 代理；保留候选/目标前后摘要、原始命令、SQL/API 快照、页面诊断及清理/凭据扫描。父级完整验证在最终冻结后重新执行上述准备，不能把 child 的旧输入当成最终输入；普通完整验证不要求 clean。

Document 初版身份升级采用已确认的开发数据清理前提：固定 baseline 真实建立旧 Document 后，候选 Host 必须在新 step 的 DDL/history 前因非空旧 Document 拒绝。fixture 只清理自身创建的旧 Document 及其 Workspace 入向引用，然后在原 catalog 重试正式 Host 升级；Part、Folder、Identity 与旧历史前缀保持，升级后重新创建 Document 验证完整首版链。该过程不证明旧 Document 数据连续，也不重置整库。

开发时可只开最低层或最短页面入口，二者默认明确 skip；完整 Workspace Gate 仍由父级集中执行。页面入口沿正式 driver 走进入、旧列表/属性、图文档创建、属性、编辑、删除和结果，不重复已通过的父成员生命周期循环：

```powershell
$env:NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE = '1'
dotnet test tests/acceptance/Acceptance.Tests/Acceptance.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~NativeWorkspaceApi_UsesFixedRelease' --logger 'console;verbosity=detailed'
dotnet test tests/acceptance/Acceptance.Tests/Acceptance.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~NativeWorkspaceUninstall_RecoversExistingBusiness' --logger 'console;verbosity=detailed'
dotnet test tests/acceptance/Acceptance.Tests/Acceptance.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~NativeWorkspace_UsesFormalDriver' --logger 'console;verbosity=detailed'
Remove-Item Env:NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE
```

父组合屏蔽/恢复保留成员自身选择，以同一个正常判权的 LocalAdmin 和活动目录验证可用性。按 ADR-0048，正式删除的活动功能不恢复普通角色旧授权；fixture 如实保留恢复后的旧普通账号 403，再通过原有限授权准备流程另建页面主体，不宣称原授权恢复。退出/恢复验证复用 `PublishedComponentAcceptanceFixture.FromWorkspaceDelivery`、`PrepareAsync`、`RestartAsync` 和同一正式 Workspace runner/driver，不另建平行部署或浏览器准备。

## 100 组件、5 层规模回归

`ComponentBundleScaleTests` 是长期低层入口。临时输入只有组合声明，五层实际分布为 `1+4+16+48+31`；从正式 Catalog 读取的声明图与最终活动目录各自计算层数。它通过正式 CLI build/pack/inspect，再走真实原生安装、preflight、候选 compiler、RuntimeViewMaterializer，核对实际活动目录 100 个组件/5 层。真实业务 SQL/API/页面由上面的三成员纵向入口证明，规模测试不宣称 100 个业务页面或 SQL 模型。

```powershell
$repo = (Get-Location).Path
dotnet build server/tools/component-bundle/ComponentBundle.csproj -c Release
$env:NEWP_COMPONENT_SCALE_TOOL = (dotnet msbuild server/tools/component-bundle/ComponentBundle.csproj -nologo -p:Configuration=Release -getProperty:TargetPath).Trim()
$env:NEWP_COMPONENT_SCALE_EVIDENCE = Join-Path $repo ('.codex-temp/component-scale/' + [guid]::NewGuid().ToString('N'))
$env:NEWP_RUN_COMPONENT_SCALE = '1'
dotnet test tests/PluginPlatform.Tests/PluginPlatform.Tests.csproj -c Release --filter 'FullyQualifiedName~ComponentBundleScaleTests' --logger 'console;verbosity=detailed'
Remove-Item Env:NEWP_RUN_COMPONENT_SCALE, Env:NEWP_COMPONENT_SCALE_TOOL, Env:NEWP_COMPONENT_SCALE_EVIDENCE
```

未启用开关时 xUnit 明确记录 skip，父级默认 verify 的 skip 不替代本次要求的显式执行。证据目录只保留命令、声明/活动目录、各阶段耗时及清理结果；临时源码/包/库存均在本例归还。100/5 是回归场景，不是产品容量上限，也不增加性能阈值。

## 兼容代码边界

仓库仍有少量 `ModuleBase`、`FrontendModule`、Host 私有 `plugin.json` 和旧示例模块，它们是迁移兼容路径，不是新增模块模板。普通接入不得：

- 复制 `ModuleTemplateSample` 的 legacy 前端声明；
- 在 Shell 增加业务模块数组或 module id 特判；
- 在 Host 新建私有模块清单；
- 让 metadata 与代码分别声明同一条路由、菜单或权限；
- 在模块注册副作用中隐式迁移或导入数据。

底层契约见[module-contract.md](../architecture/module-contract.md)，模板结构见[module-template.md](../architecture/module-template.md)。
