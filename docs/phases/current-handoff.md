# 当前交接台账

最近更新：2026-06-06

## 1. 目的

本文档用于承接“当前现状、上一次做了什么、下一步推荐做什么、还有哪些未解决问题”这类交接信息。

它只维护当前快照，不替代详细设计、实施计划、阶段地图或验证记录。需要判断设计边界、任务步骤和验证细节时，仍应回到对应源文档。

## 2. 使用方式

开始新任务时建议按以下顺序阅读：

1. `AGENTS.md`
2. `docs/phases/current-handoff.md`
3. `docs/phases/post-phase-6-stage-map.md`
4. 当前任务相关的设计、计划、模块说明和验证记录

每次完成一个工作包或一次明确收口后，应回填本文档的：

- 最近完成
- 当前推荐下一步
- 未解决问题
- 验证快照
- 已知风险与回滚思路

## 3. 当前事实源

- 仓库级规则入口：`AGENTS.md`
- 当前交接快照：`docs/phases/current-handoff.md`
- 阶段主线入口：`docs/phases/post-phase-6-stage-map.md`
- 新增设计与计划入口：`docs/superpowers/README.md`
- 验证口径入口：`docs/verification/verification-matrix.md`
- 历史状态归档：`docs/current-status.md`

如果本文档与具体设计、计划或验证记录冲突，以更具体、更新的任务文档为准，并在下一次收口时修正本文档。

## 4. 最近完成

### 4.9 标准 CRUD 通用 XLSX 导出与抽屉 URL 同步

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-06-01-standard-crud-xlsx-export-and-drawer-url-design.md`
- `docs/superpowers/plans/platform/2026-06-01-standard-crud-xlsx-export-and-drawer-url.md`

完成内容：

- 标准 CRUD 后端新增通用 `/export` 路由、导出请求模型、导出服务和最小 XLSX writer。
- 导出列由前端运行时 `view.columns` 投影提交，但后端仍按 descriptor `FieldSet.ListFields` 白名单裁剪，避免模块或前端把未授权字段导出。
- 前端 SDK 新增标准 CRUD `exportList` 调用，Shell List Host 根据 `export.modes` 渲染 `导出当前 / 导出全部` 入口。
- 标准 CRUD 列表抽屉打开与关闭已同步 `drawer/id` URL 参数，关闭时保留无关查询参数并清理抽屉参数。

验证结论：

- AI-first 设计校验和 target 检查通过。
- `PlatformCrud.Tests` 中 StandardCrud 相关测试通过。
- Shell `standard-crud-list-host.test.tsx` 与 `person-center-crud-pro-sample.test.tsx` 通过，Shell build 通过。

当前推荐下一步：

- 先处理标准 CRUD 查询边界问题：当前疑似存在列表查询每次按全量数据和全字段读取的风险，应独立设计并验证分页、筛选、排序、projection 字段裁剪和导出 `current/all` 查询边界，再继续扩第二个资源试点。

### 4.8 人员中心体验完善包 A

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-27-person-center-experience-polish-design.md`
- `docs/superpowers/plans/platform/2026-05-27-person-center-experience-polish.md`
- `docs/modules/organization-implementation.md`

完成内容：

- 人员中心行动作支持 `visibleWhen` 状态条件，避免启用、停用等互斥动作同时展示。
- 标准 CRUD 操作列、危险动作收纳、错误透传和列表接口状态体验完成一轮收敛。
- Organization service 补充后端业务错误透传，人员中心删除绑定账号人员时不再只显示泛化错误。
- `docs/modules/organization-implementation.md` 补充人员中心标准 CRUD 样板入口、边界、验证入口、失败信号和回滚口径。

验证结论：

- Shell 定向测试、Organization service 错误处理测试、Shell build、Organization 前端 build、Organization 后端测试通过。
- AI-first 设计校验和 target 检查通过。

当前推荐下一步：

- 人员中心体验包 A 已收口；后续体验细化、动作级权限、`PersonNo` 数据库唯一索引和高级筛选不应混入下一轮查询边界修复。

### 4.7 标准 CRUD 元数据回收

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md`
- `docs/superpowers/plans/platform/2026-05-24-standard-crud-metadata-recovery.md`
- `docs/verification/records/2026-05-24-standard-crud-metadata-recovery.md`

完成内容：

- 标准 CRUD 元数据回收实施完成：人员中心查询、默认查询、列表列、页面动作、行动作、详情 sections、summaryBlocks 和表单字段已优先从 `PersonList.view.json / PersonDetail.view.json / Person.model.json` 合成；Organization 前端注册仅保留 provider、renderer、handler、mapper。
- Shell 标准 CRUD schema resolver 补齐 metadata column/action/query/form 校验，并由 List Host / Detail Host 统一消费 metadata schema、metadata form 和 metadata query。
- 人员中心资源注册删除长期字段数组、表单分组、列表列、筛选组件、标准动作数组和私有 mutation mapper；私有 `personCenterRuntime` fallback 已退场。
- 人员中心旧新建页改为兼容提示，实际新建入口由标准 CRUD 列表抽屉承接。
- 后端 Person descriptor 和标准 CRUD 读取测试补齐 `DepartmentName / PositionName` projection 字段契约，确保已声明 projection 字段稳定返回。

验证结论：

- AI-first 设计校验、metadata target 检查、dic localization 校验通过。
- Shell 标准 CRUD 定向测试与 shell build 通过；Organization / PlatformCrud 后端定向测试通过。
- Shell build 仍输出既有 Vite chunk size warning；jsdom 测试仍输出既有 AntD / React Router 警告，未作为本轮失败信号。

当前推荐下一步：

- 基于人员中心样板评估第二个标准 CRUD 资源，但在复制前先确认是否需要树形 lookup 或通用 model option endpoint。

### 4.6 人员中心界面二次微调

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-23-shell-crud-workspace-visual-adjustment-design.md` 第 14.1 节

完成内容：

- 人员中心列表去掉公司筛选和公司列，保留创建、详情、编辑表单里的公司字段与组织级联模型。
- 标准 CRUD 单列表页在无标题/描述时把主动作并入筛选行，避免单独一整行只放“新建人员”。
- 二次反馈后，主动作区已回到筛选区上方命令条，承接最多 5 个常驻功能按钮；筛选区不再拉长关键字搜索。
- Sidebar 菜单投影开始渲染 `icon` 元数据，并为内建 navigation metadata 补齐轻量图标声明。
- 标准 CRUD 表格分页移动到工作面底部，页大小下拉保留右侧安全间距，并固定提供 `20 / 50 / 100 / 200`。
- 标准 CRUD 表格默认提供前端排序入口和表头列宽拖拽手柄。
- 人员中心列表补充多选框、序号列和人员图标列；序号/图标辅助列不显示列名并使用紧凑宽度，多选只保留 UI 选中状态，暂未绑定批量动作。
- 标准 CRUD 表格已输出奇偶行 class，Shell 侧提供轻量隔行底色，hover 底色优先覆盖隔行底色。
- 标准 CRUD 行操作最多显示 3 个按钮，`更多` 计入 3 个按钮名额；人员中心当前展示为 `查看 / 编辑 / 更多`，`删除 / 停用` 收进更多中并保留确认流程。

验证结论：

- 定向测试、UI/Shell/Organization 前端构建、设计校验和浏览器 smoke 均已通过。
- Shell 构建仍输出既有 Vite chunk size warning；jsdom 测试仍输出既有 AntD / React Router 警告，未作为本轮失败信号。

### 4.5 人员中心后台 CRUD Pro 样板固化

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-17-person-center-crud-pro-sample-design.md`
- `docs/superpowers/plans/platform/2026-05-18-person-center-crud-pro-sample.md`
- `docs/verification/frontend-ant-design-rebuild-verification.md`

完成内容：

- 人员中心作为平台第一版完整后台 CRUD 样板，覆盖列表、详情抽屉和表单抽屉。
- 新增、查看、编辑默认在右侧抽屉完成，独立详情页保留为深链接和回滚出口。
- 标准动作集收敛为 `create / refresh / view / edit`；平台提供 `delete` 危险动作协议能力，但只有资源显式声明并具备后端能力时才展示；`disable` 明确为可选标准动作。
- 人员中心本轮显式展示删除入口；删除调用既有标准 CRUD DELETE，已绑定账号时由后端条件删除 guard 拒绝。

当前推荐下一步：

- 围绕人员中心 CRUD Pro 样板选择下一批可复制试点，或先拆包补齐删除权限细化、业务冲突提示等边界设计。
- 如果继续 Ant Design 页面重做主线，应先补对应页面族设计/计划后再选择配置管理、权限中心、文件中心等页面，不再把配置管理作为无条件默认下一步。

### 4.0 前端 Ant Design 重建阶段收口

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-17-frontend-ant-design-rebuild-design.md`
- `docs/superpowers/plans/platform/2026-05-17-frontend-ant-design-rebuild.md`
- `docs/verification/frontend-ant-design-rebuild-verification.md`

完成内容：

- Shell 已引入 Ant Design 外壳，`App.tsx` 注入 `ConfigProvider` 和企业级主题 token。
- Shell Layout、Header、Sidebar 与 PageContainer 已改造为 Ant Design 结构，仍保留 runtime metadata / ModuleRegistry 菜单来源、权限过滤、路由承载、`Outlet` 和 `ErrorBoundary`。
- `frontend/packages/ui` 已新增 Pro Components adapter：`SearchTablePage`、`FormPage`、`DetailPage`、`ActionButtons`、`StatusTag`、`AlertState`、`LoadingBlock`；Pro Components 继续被平台 adapter 包住，不替代平台契约。
- 人员中心标准 CRUD 列表和详情已接入 table/detail/form adapter，保留 `buildListQuery`、`mapListItem`、`getRowId`、lookup、summary、权限动作和保存/停用行为。
- 日志审计的操作日志、访问日志已作为低风险页面改造成查询表格样板，保留 `/api/audit/operation-logs` 与 `/api/audit/access-logs` 分页接口语义。
- Shell 测试增加 AntD jsdom setup，避免后续每个老测试重复维护 `matchMedia` / pseudo `getComputedStyle` 补丁。

当前推荐下一步：

- 如继续 Ant Design 页面重做主线，应先补对应页面级设计/计划，再从配置管理、权限中心、文件中心、插件运维、元数据配置中心中选择下一包。
- 该阶段建议已被 4.5 和第 5 节更新：配置管理不再作为当前无条件默认下一步。

### 4.0 人员中心 metadata runtime 入口修复

状态：已完成并验证。

完成内容：

- Host 现在会把内建模块 metadata 投影到 `plugins/runtime/modules`，并暴露 `/plugins/metadata-runtime-manifest.json` 与 `/plugins/runtime/modules/.../metadata/...`。
- Host 项目输出不再只复制 `.model` 元数据，已覆盖 `modules/*/metadata/**/*.json`，保证 Organization 的 navigation/view/model 都能进入运行时视图。
- Vite 开发态和构建态会生成同口径的内建 metadata runtime manifest、localization manifest 与静态 metadata 文件，避免开发态 `/organization/persons` 因 metadata 404 落回旧占位页，同时避免 metadata 菜单接管后退化成文案 key。
- `MapStandardCrudResources()` 改为只在标准 CRUD 注册表存在时挂载，避免纯外部插件装载视图缺少标准 CRUD 服务时打断 Host 启动。

当前推荐下一步：

- 人员中心若再次出现“请通过 metadata 入口访问”的占位提示，优先检查 `/plugins/metadata-runtime-manifest.json`、`/plugins/runtime/modules/Organization/metadata/navigation/Organization.nav.json` 和 `views/PersonList.view.json` 是否可访问。

### 4.1 前端标准 CRUD 平台化第二批

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md` 第 14.2 节
- `docs/superpowers/plans/platform/2026-05-16-frontend-standard-crud-platformization-phase-2.md`
- `docs/verification/frontend-standard-crud-platformization-phase-2-verification.md`

完成内容：

- SDK 提供字段、表单、lookup、动作、summary 和 i18n 第二批契约与 helper。
- UI 提供无业务语义的标准 CRUD 字段和表单骨架。
- Shell 接管 lookup 加载、动作状态、权限动作过滤、summary 状态协议和详情编辑流程。
- Organization 人员中心接入第二批平台能力，通用字段渲染、级联 lookup 状态机和 summary 状态协议不再散落在人员组件中。

当前推荐下一步：

- 选择下一批标准 CRUD 资源试点，或继续拆解后端 required field / 数据一致性主线。

### 4.2 前端标准 CRUD 平台化第一批

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md`
- `docs/superpowers/plans/platform/2026-05-12-frontend-standard-crud-platformization.md`
- `docs/verification/frontend-standard-crud-platformization-phase-1-verification.md`

完成内容：

- SDK 提供标准 CRUD 前端契约和客户端 helper。
- Shell 提供标准 CRUD schema resolver、registry、列表 Host 和详情 Host。
- Organization 人员中心接入标准 CRUD 前端资源注册。
- 人员中心列表/详情主状态机不再留在模块私有页面中。

当前推荐下一步：

- 第一批列表/详情 Host 已稳定接管；第二批可独立回滚，不影响第一批列表/详情 Host。

### 4.3 标准 CRUD 路由自动映射与人员旧样板退场

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-11-standard-crud-route-mapping-design.md`
- `docs/superpowers/plans/platform/2026-05-11-standard-crud-route-mapping.md`
- `docs/verification/organization-module-verification.md`

完成内容：

- 平台新增标准 CRUD 资源注册表，`StandardCrudDescriptor.Route` 成为标准 API 路由唯一真源。
- Host 通过 `MapStandardCrudResources()` 自动挂载标准 CRUD 路由，`/api/organization/persons` 不再依赖人员标准 CRUD Controller。
- Organization 只注册 Person descriptor；人员账号绑定拆为专属 `PersonUserLinksController` 与 `PersonUserLinkService`。
- 人员中心后端旧标准 CRUD Controller / Service / Repository / DTO 样板已删除。
- 人员中心前端已改为消费标准 CRUD list/detail/mutation 返回模型。
- 验证记录已回填标准 CRUD 自动映射入口、旧样板残留扫描和前后端构建测试结论。

最近相关提交：

- `94eba99f`：功能：新增标准CRUD资源注册表
- `0943e9ec`：功能：新增标准CRUD自动路由映射
- `a7ea752d`：功能：拆出人员账号绑定服务
- `570768c2`：重构：人员中心标准CRUD改由平台自动映射
- `345efed0`：重构：人员中心前端消费标准CRUD模型
- `65494edd`：验证：记录标准CRUD自动映射收口结果

### 4.4 EID 值协议平台化

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-08-eid-value-protocol-design.md`
- `docs/superpowers/plans/platform/2026-05-10-eid-value-protocol.md`
- `docs/verification/organization-module-verification.md`

完成内容：

- 新增平台对象 ID 生成器，平台对象新建 `EID` 统一使用 `{ModelName}_{Guid:N}`。
- `PlatformCrud`、`ConfigCenter`、`Authorization`、`Organization` 的新建路径不再生成裸 GUID `EID`。
- `Organization` 人员、组织主数据和人员账号关联的主键与引用字段已按字符串 ID 流转。
- API 路由已移除 `{id:guid}` 约束。
- `PersonUserLink` 已改为独立 EID，避免继续复用人员或账号 ID 作为关联主键。

最近相关提交：

- `c921d527`：设计：制定EID值协议平台化方案
- `25f90c7f`：功能：新增平台对象ID生成器
- `45cfba2c`：功能：ConfigCenter接入平台对象ID
- `974c4604`：功能：Authorization接入平台对象ID
- `6e486f84`：功能：Organization收口平台对象ID
- `3e8faa55`：功能：人员账号关联使用独立EID
- `72807119`：验证：记录EID值协议平台化结果

## 5. 当前推荐下一步

人员中心标准 CRUD 元数据回收、体验包 A、通用 XLSX 导出与抽屉 URL 同步均已完成。当前最高优先级不再是马上复制第二个标准 CRUD 资源，而是先解决标准 CRUD 查询边界问题：当前疑似存在列表接口每次全量查询、未按分页裁剪、并读取全部字段的风险。该问题会直接影响列表性能、导出边界、projection 契约和下一批资源试点的可复制性。

下一轮应先补详细设计和实施计划，目标是把标准 CRUD 的分页、筛选、排序、projection 字段裁剪、导出 `current/all` 行为边界和失败信号收口到平台内核。未完成该收口前，不建议扩展第二个标准 CRUD 资源。

推荐优先级：

1. 设计并验证标准 CRUD 查询边界：确认当前实现是否全量查数据、是否全字段读取；修复后端 list query 的分页、筛选、排序、projection 字段裁剪，并明确导出 `current/all` 的查询上限和字段白名单。
2. 查询边界修复完成后，再基于人员中心样板评估第二个标准 CRUD 资源；复制前仍需确认是否需要树形 lookup 或通用 model option endpoint。
3. 选择下一批标准 CRUD 资源试点时，应沿用 metadata 声明标准查询、列、字段、动作的形态；代码注册只保留 provider、renderer、handler、mapper 等执行扩展。
4. 单独设计人员删除独立权限、业务冲突提示和后端 guard 失败展示细化，不混入查询边界修复。
5. 若继续 Ant Design 页面重做主线，再按页面族拆包选择配置管理、权限中心、文件中心、插件运维或元数据配置中心。
6. 若继续标准 CRUD 后端主线，required field / 模型必填校验应另起设计，避免和查询边界修复互相扩范围。
7. 若继续数据一致性主线，单独设计 `PersonNo` 数据库唯一索引或并发强一致性策略。
8. 若继续 EID 主线，单独设计历史裸 GUID 数据迁移；当前开发数据库仍按空库重建口径处理。
9. 若继续平台元数据主线，再拆包吸收 `docs/designs/platform/review-open-items-tracker.md` 中“已冻结，待计划吸收”的问题。
10. 若继续业务验证主线，单独拆包处理真实业务页消费接入或下一批 Dictionary / Enumeration 样本扩展。

## 6. 未解决问题

以下问题已知存在，但当前不应混在同一次改动中顺手处理。

### 6.1 待计划吸收的 review 结论

来源：`docs/designs/platform/review-open-items-tracker.md`

- `.dic` 消费链仍需对齐 authoring 统一规则。
- `.model` 消费链仍有固定目录或固定文件路径依赖。
- `.settings` 消费链仍主要依赖单一 `SourcePath`。
- 组织模块人员中心试点实施计划必须吸收已冻结的首轮边界。

### 6.2 EID 后续风险

来源：`docs/verification/organization-module-verification.md`

- 本轮没有做历史裸 GUID 数据回填；如需导入旧库数据，应单独设计迁移。
- 当前开发数据库按空库重建处理，不承诺旧裸 GUID 主键数据兼容。

### 6.3 标准 CRUD 后续风险

来源：`docs/verification/organization-module-verification.md`

- 当前疑似存在标准 CRUD list 查询按全量数据和全字段读取的风险；下一轮应优先用测试或日志确认实际 SQL / 查询路径，再修正分页、projection 和导出边界。
- `ENAME` 等字段必填校验仍需要后续标准 CRUD required field 或模型必填校验设计承接。
- `PersonNo` 当前由标准 CRUD 服务层保证全局唯一，但未引入数据库唯一索引；并发强一致性如需支持，应另开设计。
- 标准 CRUD 自动路由映射当前已由 Person 试点验证；下一批资源接入前仍应逐个确认 descriptor 路由、字段边界和模块专属能力是否拆分清楚。

## 7. 验证快照

标准 CRUD 通用 XLSX 导出与抽屉 URL 同步验证覆盖：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-01-standard-crud-xlsx-export-and-drawer-url-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrud
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host.test.tsx person-center-crud-pro-sample.test.tsx
pnpm --dir frontend --filter @icp-platform/shell build
```

验证结论：

- 标准 CRUD 导出路由、字段白名单、当前页导出、全部导出分页归一化和前端导出入口已有定向覆盖。
- 抽屉 URL 同步已有创建、查看、编辑、关闭清理参数的前端定向覆盖。
- 查询边界仍需下一轮补强：当前验证尚不足以证明列表接口没有全量查数据或全字段读取。

人员中心体验完善包 A 验证覆盖：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry person-center-crud-pro-sample organization-person-center-list-layout
pnpm --dir frontend --filter @icp-platform/shell test -- organization-service-error-handling
pnpm --dir frontend --filter @icp-platform/shell build
pnpm --dir frontend --filter @icp-modules/organization build
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-27-person-center-experience-polish-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

验证结论：

- 行动作状态条件、危险动作收纳、操作列、业务错误透传和人员中心样板文档已有定向覆盖。
- 该验证不覆盖标准 CRUD 查询是否按后端分页和 projection 裁剪执行。

标准 CRUD 元数据回收验证覆盖：

```powershell
./scripts/validate-ai-first-design.ps1 -Path docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md
./scripts/check-ai-first-design-targets.ps1
./scripts/validate-dic-localization.ps1
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query standard-crud-list-host standard-crud-detail-host person-center-crud-pro-sample organization-person-phase2-shrink
pnpm --dir frontend --filter @icp-platform/shell build
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

验证结论：

- AI-first 设计校验、metadata target 检查和 dic localization 校验通过。
- `@icp-platform/shell` 标准 CRUD 定向测试通过，7 files / 54 tests。
- `@icp-platform/shell` 构建通过，`tsc && vite build` 成功。
- Organization 后端测试通过，43 tests；PlatformCrud 后端测试通过，58 tests。
- Shell 构建仍输出既有 Vite chunk size warning；jsdom 测试仍输出既有 AntD / React Router 警告，未作为本轮失败信号。

人员中心界面二次微调验证覆盖：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- organization-person-center-list-layout antd-shell-layout
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-modules/organization build
pnpm --dir frontend --filter @icp-platform/shell build
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-23-shell-crud-workspace-visual-adjustment-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

验证结论：

- `@icp-platform/shell` 定向测试通过，2 files / 7 tests。
- `@icp-platform/ui`、`@icp-modules/organization`、`@icp-platform/shell` 构建通过。
- AI-first 设计校验通过。
- 浏览器 smoke 确认 `/organization/persons` 已移除公司筛选/公司列、菜单图标可见、主动作位于筛选区上方、搜索输入未被拉长、表头排序入口可见、多选框/紧凑序号列/紧凑人员图标列可见且辅助列表头为空、标准表格奇偶行 class 可见、行操作显示 `查看 / 编辑 / 更多` 且 `删除 / 停用` 收进更多、分页位于工作面底部且页大小下拉包含 `20 / 50 / 100 / 200`。
- Shell 构建仍输出既有 Vite chunk size warning；jsdom 测试仍输出既有 AntD / React Router 警告，未作为本轮失败信号。

前端 Ant Design 重建阶段收口验证覆盖：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-platform/shell build
```

验证结论：

- `@icp-platform/shell` 测试通过，41 files / 141 tests。
- `@icp-platform/ui` 构建通过，`tsc` 成功。
- `@icp-platform/shell` 构建通过，`tsc && vite build` 成功。
- Vite 构建仍输出 Ant Design / Pro Components 引入后的 chunk size warning；该警告不影响本轮构建结果，后续如需优化应单独设计 code splitting 或 manual chunks。
- jsdom 测试中 Ant Design / Pro Components 会输出 `findDOMNode`、`contentStyle` deprecation、React Router future flag 等警告；断言通过，未作为本轮失败信号。
- 尚未覆盖页面：配置管理、权限中心、文件中心、插件运维、元数据配置中心。

人员中心后台 CRUD Pro 样板固化验证覆盖：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout organization-person-center-detail-layout standard-crud-sdk
pnpm --dir frontend --filter @icp-platform/sdk build
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-platform/shell build
pnpm --dir frontend --filter @icp-modules/organization build
```

验证结论：

- `@icp-platform/shell` 定向测试通过，4 files / 27 tests。
- `@icp-platform/sdk`、`@icp-platform/ui`、`@icp-platform/shell`、`@icp-modules/organization` 构建通过。
- Shell 构建仍输出既有 Vite chunk size warning；jsdom 测试仍输出 AntD / Pro Components 与 React Router 的既有警告，未作为本轮失败信号。

人员中心 metadata runtime 入口修复验证覆盖：

```powershell
dotnet test tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~HostApiPluginManifestRouteTests"
pnpm --dir frontend --filter @icp-platform/shell test -- vite-config runtime-metadata-loader page-entry-routing standard-list-page-host
pnpm --dir frontend --filter @icp-platform/shell build
Invoke-WebRequest -UseBasicParsing -Uri http://localhost:3000/plugins/localization-manifest.json
Invoke-WebRequest -UseBasicParsing -Uri http://localhost:3000/plugins/runtime/modules/Organization/metadata/navigation/Organization.nav.json
Invoke-WebRequest -UseBasicParsing -Uri http://localhost:3000/plugins/runtime/modules/Organization/metadata/views/PersonList.view.json
Invoke-WebRequest -UseBasicParsing -Uri http://localhost:5000/api/organization/persons
```

验证结论：

- Host API manifest 路由测试通过，覆盖无插件状态下的内建 Organization metadata 投影，以及启用插件包时的 metadata manifest / nav 文件访问。
- Shell metadata、page entry、standard list host 与 Vite metadata/localization runtime 相关定向测试通过，16 tests。
- `@icp-platform/shell` 构建通过，产物中包含 `plugins/metadata-runtime-manifest.json`、`plugins/localization-manifest.json`、Organization nav/view/model metadata。
- 本地 `http://localhost:3000` 已能访问 Organization nav、PersonList view metadata 与含“人员中心”的 localization manifest，`http://localhost:5000/api/organization/persons` 返回标准 CRUD 列表数据。

最近一次前端标准 CRUD 第二批验证覆盖：

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|employmentStatusOptions|resolveListText|resolveDetailText|AuthorizationSummaryCard|PersonUserLinkCard" frontend/modules/organization/src frontend/packages
```

验证结论：

- `@icp-platform/sdk` 构建通过。
- `@icp-platform/ui` 构建通过。
- `@icp-platform/shell` 测试通过，38 files / 131 tests。
- `@icp-platform/shell` 构建通过。
- `@icp-modules/organization` 构建通过。
- 残留扫描中，`getCompanies/getDepartments/getPositions` 保留在 Organization service、人员资源 lookup providers、组织树和岗位管理页面中；人员表单 lookup 已通过标准 provider/runtime 消费，不再在 `PersonEditorForm` 或创建页散落通用状态机。
- `employmentStatusOptions` 保留为人员资源 enum option 数据和旧标签 helper 数据源。
- `PersonUserLinkCard`、`AuthorizationSummaryCard` 保留为 Organization 专属 summary renderer 内容组件，平台只处理状态协议。
- `PersonEditorForm` 只剩兼容包装，显示“人员编辑表单已由标准 CRUD 表单接管。”
- `resolveListText/resolveDetailText` 无命中。

前端标准 CRUD 第一批验证覆盖：

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "StandardCrudListResult|StandardCrudDetail|normalizeSummaryBlock|normalizePersonListResponse|normalizePersonDetail|getPersonListRuntimeSchema|getPersonDetailRuntimeSchema" frontend/modules/organization/src frontend/packages
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|PersonUserLinkCard|AuthorizationSummaryCard" frontend/modules/organization/src
```

验证结论：

- SDK、UI、Shell 和 Organization 前端构建通过，Shell 测试通过。
- 标准 CRUD 类型与 helper 已由 SDK 提供，Shell Host 已接管人员中心列表/详情主流程。
- 第二批暂留项已在 `docs/verification/frontend-standard-crud-platformization-phase-2-verification.md` 中完成收缩解释。

上一轮标准 CRUD 自动映射验证覆盖：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonsController|IPersonService|PersonService|IPersonRepository|EfPersonRepository|PersonListItemDto|PersonDetailDto|PersonSummaryBlockDto" server/modules/Organization tests/Organization.Tests frontend/modules/organization/src
rg -n "MapStandardCrudResources|AddStandardCrudResource|IStandardCrudResourceRegistry|PersonUserLinksController|IPersonUserLinkService" apps/host-api server/platform-crud server/modules/Organization tests
```

验证结论：

- Person 标准 CRUD 路由由 `StandardCrudDescriptor.Route` 自动映射，对外仍保持 `/api/organization/persons` 模块语义路由。
- 人员中心只保留账号绑定专属 Controller，不再保留标准 CRUD 空壳 Controller。
- `server/modules/Organization`、`tests/Organization.Tests` 与 `frontend/modules/organization/src` 中未发现人员旧标准 CRUD 样板名称残留。
- 平台 CRUD 测试、Organization 测试、HostApi 构建与 Organization 前端构建均已通过；详细结果见 `docs/verification/organization-module-verification.md`。

## 8. 已知风险

- 本文档是当前快照，不应把它当作长期 authoring 真源。
- Ant Design Phase 1 仅完成 Shell chrome，模块页面内部旧 `sh-*` UI 仍会短期并存；后续应通过 Phase 2/3/4 逐步收敛，而不是在 Shell 层直接重写业务页面。
- Ant Design 引入后 shell bundle 出现 chunk size warning；若需要控制首屏体积，应单独拆分动态 import 或 Vite manual chunks，不应混入当前 UI 改造任务。
- 如果只更新计划或验证记录而不更新本文档，下一次交接可能读到过期推荐下一步。
- 历史阶段文档中仍可能出现旧推荐语，应以本文件和对应任务的最新计划、验证记录为准。
- Vite 开发态现在直接投影内建 metadata 和 localization；如果未来 runtime 资源改由 Host 统一代理，需要同步收敛 Vite 插件，避免两套投影规则漂移。
- 前端标准 CRUD 第二批仍以 Person 资源为试点，下一批资源接入前需要重新确认字段、lookup、动作、summary 和模块专属能力边界。
- 标准 CRUD 当前最重要风险是查询边界疑似不清：如果列表每次全量取数或全字段读取，会影响性能、导出边界、字段权限和下一批资源复制。
- 人员中心删除本轮仍沿用资源级权限边界；如需独立删除权限或更细业务冲突展示，应另起设计和计划。
- 人员中心当前已完成第一轮 metadata 回收；下一批资源仍需先确认 lookup 类型、option endpoint、动作权限和 projection 字段契约，不应无设计直接复制。

## 9. 回滚思路

- 如果交接口径写错，只改本文档，不需要修改原始设计、计划或验证记录。
- 如果 Ant Design Phase 1 需要回滚，应移除 `frontend/packages/shell` 中的 `antd` / `@ant-design/icons` 依赖，回退 `App.tsx`、`components/Layout.tsx`、`components/Header.tsx`、`components/Sidebar.tsx`、`components/PageContainer.tsx`、`config/antdTheme.ts`、`navigation/menuProjection.tsx`、`navigation/pageChrome.ts`、`index.css` 和对应测试更新。
- 如果本次人员中心 metadata runtime 修复需要回滚，应反向恢复 `apps/host-api/Program.cs`、`apps/host-api/HostApi.csproj`、`frontend/packages/shell/vite.config.ts` 及对应测试；回滚后开发态会重新依赖旧 fallback route，人员中心可能再次显示占位错误。
- 如果前端标准 CRUD 第二批需要回滚，可反向回滚第二批 SDK/UI/Shell 运行时和 Organization 接入提交；第二批可独立回滚，不影响第一批列表/详情 Host。
- 如果标准 CRUD 自动映射需要回滚，应按本轮最近相关提交反向 revert，并优先恢复 `PersonsController` 等旧样板前确认对应设计、计划和验证记录中的边界变化。
- 如果 EID 值协议需要回滚，应按最近相关提交反向 revert，并优先回到对应设计和计划确认影响范围。
- 如果下一步方向选择变化，先更新本文件第 5 节，再补对应设计或计划，避免多个入口同时表达不同推荐。
- 如果人员中心 CRUD Pro 样板需要回滚，应反向回退本轮 UI CRUD Pro adapter、Shell standard CRUD Host 抽屉状态机、路由兼容和人员资源动作声明；独立详情页仍可作为深链接和回滚入口。
- 如果标准 CRUD 元数据回收需要回滚，应反向回退 Shell metadata resolver/Host 改动，恢复 `personStandardCrudResource.tsx` 中字段、列、查询、动作声明，并反向回退 `modules/Organization/metadata/**` 中本轮 schema 调整。
- 如果人员中心体验完善包 A 需要回滚，应回退 `PersonList.view.json` 的 `visibleWhen`、Shell Host 行动作投影、Organization 服务错误解析和 `docs/modules/organization-implementation.md` 中新增样板说明。
- 如果标准 CRUD 通用导出与抽屉 URL 同步需要回滚，应删除新增导出模型、writer、服务、`/export` 路由和 SDK `exportList`，回退 Host 导出按钮与 URL 同步逻辑；原有 `export.modes` metadata 解析可保留，不影响既有页面。
