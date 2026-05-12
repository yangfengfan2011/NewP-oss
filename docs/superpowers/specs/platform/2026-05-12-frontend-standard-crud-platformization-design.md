# 前端标准 CRUD 平台化设计

## 1. 背景

人员中心后端已经完成标准 CRUD 平台化：标准资源通过 descriptor 注册，Host 通过平台路由映射暴露模块语义 API，人员中心后端旧 Controller / Service / Repository 样板已经退场。

前端目前只完成了第一层适配：人员中心已经改为消费标准 CRUD 返回模型，但页面结构、数据适配、运行时 view 解析、列表状态、详情加载、表单字段、筛选、动作和摘要块仍集中在 Organization 模块内。典型文件包括：

- `frontend/modules/organization/src/services/organizationService.ts`
- `frontend/modules/organization/src/runtime/personCenterRuntime.ts`
- `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`
- `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- `frontend/modules/organization/src/components/PersonEditorForm.tsx`

Shell 和 UI 包已经具备部分基础能力，例如 `StandardListPageHost`、`StandardListPage`、`standardListViews` 注册和 runtime metadata 装载。这说明前端平台化不需要从零开始，但需要把人员中心中已经验证过的通用部分提升为平台能力，并明确哪些人员专属能力继续留在 Organization 模块。

## 2. 目标

- 将前端标准 CRUD 平台化拆成两批推进，避免一次性抽象过大。
- 第一批先抽出标准 CRUD 前端客户端、标准返回模型适配、运行时 view schema 解析、标准列表页 Host 和标准详情页 Host。
- 第二批明确登记为后续必做范围，覆盖字段渲染器、数据源 lookup、动作扩展、摘要块渲染、权限动作过滤和通用 i18n key 解析。
- 保持 Shell 只负责装配、路由聚合和宿主级页面入口，不承载人员中心私有业务规则。
- 保持 Organization 模块只声明资源配置、字段扩展、专属动作和专属摘要，不再复制标准 CRUD 页面流程。
- 为后续实施计划提供清晰入口、边界、验证、失败信号和回滚方式。

## 3. 非目标

- 本设计不直接进入实现，不修改前端代码。
- 不重做后端标准 CRUD 内核、descriptor 或 API 路由映射。
- 不把人员账号绑定、授权摘要、组织归属业务语义变成平台内置能力。
- 不在第一批实现完整表单设计器、拖拽布局、复杂页面编排器或远程插件市场。
- 不要求一次性改造 ConfigCenter、Authorization、Audit 等所有既有模块。
- 不改变前端插件交付模型，不处理远程前端插件签名、版本矩阵或市场分发。

## 4. 范围

第一批允许涉及：

- `frontend/packages/sdk/src/`
  - 新增标准 CRUD 前端契约、返回模型、客户端帮助器和纯数据适配函数。
- `frontend/packages/shell/src/metadata/`
  - 泛化 view metadata 解析，替代人员专属 runtime schema 读取链路。
- `frontend/packages/shell/src/pages/`
  - 新增或改造标准 CRUD 列表页 Host、详情页 Host。
- `frontend/packages/shell/src/navigation/`
  - 如确有需要，扩展 runtime page entry 类型，用于标准详情页或标准 CRUD 页入口。
- `frontend/packages/shell/src/registries/`
  - 扩展标准 CRUD 注册表，承接模块提供的资源配置和扩展点。
- `frontend/packages/ui/src/patterns/`
  - 只补纯展示型列表、详情、筛选和状态模式组件，不写模块业务规则。
- `frontend/modules/organization/src/`
  - 作为试点模块收缩人员中心标准流程代码，保留人员专属扩展。
- `frontend/packages/shell/src/__tests__/`
  - 补 Host、metadata、注册表和人员试点回归测试。

第二批允许涉及：

- `frontend/packages/sdk/src/`
  - 字段渲染、动作扩展、数据源、摘要块扩展的公开契约。
- `frontend/packages/shell/src/`
  - lookup 装配、动作权限过滤、summary block 装配、详情编辑策略。
- `frontend/packages/ui/src/components/` 与 `frontend/packages/ui/src/patterns/`
  - 通用字段控件、表单布局、摘要块默认渲染。
- `frontend/modules/organization/src/`
  - 继续删除人员表单、lookup、摘要块中已经被平台承接的通用逻辑。

明确不在本设计范围内：

- `server/**` 后端实现文件。
- 部署脚本、CI 流水线、密钥处理和基础设施编排。
- Shell 内部布局大改和视觉重做。

## 5. 设计约束

- 元数据只声明“是什么、归谁、允许什么”，不得把加载流程、分支算法、回滚逻辑下沉到 metadata 解释层。
- 标准 CRUD 页面流程必须收口在代码内核：客户端、schema resolver、Host、registry 和扩展点由 TypeScript 代码明确实现。
- Shell 只拥有布局、全局 Provider、菜单聚合、路由聚合和受控注册；不得写入人员中心业务规则。
- 前端模块通过 SDK 契约和受控注册点接入，不直接依赖 `shell/src/components/*` 等内部实现。
- 第一批必须保持可回退：人员中心原页面可以在失败时恢复到现有模块内实现。
- 第二批必须在第一批设计和验证记录中可追踪，不能只以口头“后续优化”存在。

## 6. 总体方案

采用“两批平台化”的方案。

第一批聚焦标准 CRUD 页面主流程：

1. 在 SDK 中沉淀标准 CRUD 前端模型与客户端帮助器。
2. 在 Shell 中建立通用 view schema resolver。
3. 在 Shell / UI 中建立标准列表页 Host，承接查询、加载、错误、空态、表格、分页和基础动作。
4. 在 Shell / UI 中建立标准详情页 Host，承接按 id 加载、标题、错误、状态、标准 page actions 和详情区块入口。
5. Organization 只提供人员资源配置、特殊字段 renderer、人员专属动作和保留的专属组件。

第二批聚焦深层复用：

1. 通用字段渲染器根据 model metadata、view metadata 和数据源声明生成编辑表单。
2. 通用 lookup 服务支持 enum、dictionary、value list、model lookup，以及受控级联依赖。
3. 动作扩展支持标准动作、模块专属动作、确认策略、权限过滤和失败提示。
4. 摘要块渲染支持标准状态协议和模块自定义组件。
5. i18n key 解析从人员组件中抽出为平台帮助器。

对比方案：

- 一次性全抽：能更快得到完整平台化表面，但会同时移动列表、详情、表单、lookup、动作和摘要块，风险高，回滚困难。
- 只做第一批：短期风险低，但会留下表单、lookup、摘要块继续复制的问题，和用户本次要求不一致。
- 两批推进：第一批先消除最大重复页面流程，第二批作为明确登记的后续范围承接深层复用，最符合当前 AI-first 小步验证目标。

## 7. 关键模型

第一批新增或扩展的模型建议如下，具体命名可在实施计划中保持同义微调，但职责不能漂移。

### 7.1 标准 CRUD 客户端模型

- `StandardCrudListResult<TItem>`
  - `items / total / page / pageSize / sortBy / sortDirection`
- `StandardCrudDetail`
  - `fields: Record<string, unknown>`
  - `summaries: Record<string, StandardCrudSummaryBlock | undefined>`
- `StandardCrudSummaryBlock<TData>`
  - `status: ready | notConfigured | dependencyUnavailable`
  - `data: TData | null`
- `StandardCrudMutationValues`
  - `Record<string, unknown>`，用于 create / update 请求体。

### 7.2 标准 view schema

- `StandardCrudListSchema`
  - `viewKey / resourceKey / route / dicKey / titleKey / descriptionKey`
  - `queryFields / advancedFilter / columns / pageActions / rowActions / exportModes`
  - `editorMode / detailViewKey`
- `StandardCrudDetailSchema`
  - `viewKey / resourceKey / route / dicKey / titleKey / descriptionKey`
  - `sections / summaryBlocks / pageActions`
- `StandardCrudFieldSchema`
  - `key / labelKey / source / field / preset / operator / dataSourceKey`

### 7.3 模块扩展注册

- `StandardCrudResourceRegistration`
  - 模块声明资源的前端消费配置，不替代后端 descriptor。
  - 包含 `resourceKey`、`listViewKey`、`detailViewKey`、`apiRoute`、扩展 renderer 和动作处理器。
- `StandardCrudFieldRenderer`
  - 处理特殊列、特殊详情字段和状态显示。
- `StandardCrudActionHandler`
  - 处理模块专属动作，例如人员账号绑定入口。
- `StandardCrudSummaryBlockRenderer`
  - 处理模块专属 summary block，例如人员账号绑定与授权摘要。

## 8. 核心流程

### 8.1 第一批列表流程

1. Runtime navigation 指向 `standard-list` 或后续等价标准 CRUD 入口。
2. Shell 通过 `viewKey` 读取 runtime view metadata。
3. 标准 schema resolver 将 view metadata 转成 `StandardCrudListSchema`。
4. 标准 CRUD registry 根据 `viewKey` 找到模块注册的资源配置。
5. 标准列表 Host 根据 schema 渲染标题、查询栏、表格列、基础动作、分页和错误态。
6. 标准 CRUD 客户端按 `apiRoute` 发起 list 请求并规范化响应。
7. 字段显示优先使用模块注册的 renderer；没有 renderer 时使用平台默认文本、布尔、枚举或引用显示。
8. 人员专属 create / edit / disable 行为在第一批可继续由模块 action handler 或现有组件承接，不强行进入通用表单。

### 8.2 第一批详情流程

1. Shell 通过标准详情入口或原详情 route 获得 `viewKey` 与 `id`。
2. 标准详情 Host 读取 detail view metadata 和资源注册。
3. 标准 CRUD 客户端加载 detail。
4. Host 渲染标题、加载态、错误态、标准 page actions 和详情区块。
5. 详情字段默认按 schema sections 只读显示；编辑内容和专属摘要在第一批通过模块扩展组件承接。
6. disable 等标准动作通过标准客户端执行，执行后刷新详情。

### 8.3 第二批表单与 lookup 流程

1. 标准表单 renderer 读取 detail schema、model metadata 和资源注册。
2. 字段类型由 model metadata 推导，字段显示名由 dicKey 和 labelKey 解析。
3. 字段数据源由 metadata 声明和代码注册共同约束，平台负责 enum / dictionary / value list / model lookup 的加载流程。
4. 级联 lookup 通过显式 dependency 声明处理，例如 Department 依赖 Company，Position 依赖 Department。
5. 表单提交前由平台做基础 required / 类型 / 空值规范化，模块仍可注册专属校验或提交前转换。

## 9. 接口与集成点

### 9.1 SDK 接口

`@icp-platform/sdk` 继续作为前端模块公共契约出口。第一批应优先新增标准 CRUD 类型和注册契约，避免模块直接引用 Shell 内部类型。

建议出口：

- `StandardCrudListResult`
- `StandardCrudDetail`
- `StandardCrudSummaryBlock`
- `StandardCrudResourceRegistration`
- `StandardCrudFieldRenderer`
- `StandardCrudActionHandler`
- `StandardCrudSummaryBlockRenderer`

### 9.2 Shell 接入点

Shell 负责：

- 从 runtime metadata 加载 view / model documents。
- 根据 runtime page entry 渲染标准 CRUD 页面 Host。
- 构建标准 CRUD registry。
- 装配模块扩展点。

Shell 不负责：

- 人员账号绑定业务规则。
- 组织树业务逻辑。
- 人员授权摘要数据含义。
- 特定资源的字段保存语义。

### 9.3 UI 接入点

`@icp-platform/ui` 只沉淀纯展示和交互骨架：

- 标准列表页布局。
- 标准详情页布局。
- 筛选栏、表格容器、分页、错误态、空态、加载态。
- 第二批的字段控件和表单布局。

UI 包不直接发请求，不读取 runtime metadata，不知道人员中心。

### 9.4 Organization 接入点

Organization 模块在第一批后应只保留：

- 人员资源前端注册。
- 人员字段 renderer。
- 人员专属 action handler。
- 人员账号绑定与授权摘要组件。
- 第一批尚未平台化的 `PersonEditorForm`。

第二批完成后，Organization 中的表单字段、lookup、summary block 通用降级逻辑应继续收缩，只保留专属语义组件。

## 10. 数据与状态

- 标准 CRUD 列表状态由 Host 管理：filters、appliedFilters、loading、error、rows、total、page、pageSize、reloadTick。
- 标准详情状态由 Host 管理：id、loading、error、detail、actionError、actionPending。
- 模块扩展组件只接收必要 props，不持有 Host 全局状态。
- Runtime metadata 是当前生效视图，不作为长期 authoring 真源；authoring 仍回到 `modules/**/metadata/**`。
- 字段值仍以标准 CRUD 后端返回的 `fields` 为准，前端 mapper 不创造新的业务真源。

## 11. 错误处理与风险

### 11.1 失败处理

- metadata 缺失：Host 显示清晰错误态，指出缺失 viewKey 或 resource registration。
- 标准 CRUD 请求失败：Host 保留列表或详情错误态，不让模块扩展组件吞掉平台错误。
- renderer 缺失：平台使用默认只读文本渲染，并在开发态输出可定位警告。
- action handler 缺失：标准动作走平台默认处理，专属动作显示不可用或不渲染。
- lookup 加载失败：第二批使用字段级降级，已选值保留为只读显示。

### 11.2 风险

- 抽象过早导致平台 Host 承载人员业务：通过扩展点边界和保留 Organization 专属组件控制。
- 第一批完成后遗忘第二批：通过本文第 14 节的第二批固定清单、验证记录要求和 handoff 回填要求控制。
- schema resolver 与现有人员 fallback 不一致：第一批保留人员试点回归测试，确保标题、筛选、列、动作和详情区块不退化。
- 前端注册契约过宽：SDK 只暴露稳定契约，Shell 内部 registry 不作为模块直接依赖。

### 11.3 回滚思路

- 第一批回滚时，恢复 Organization 现有人员中心页面入口和 `PersonListStandardListEntry` 逻辑，撤回新增标准 CRUD Host 接入。
- SDK 新增类型如果未被外部模块使用，可按提交反向回退。
- Shell runtime page entry 扩展如果导致导航异常，可先回退到现有 `standard-list` + 模块 Entry 模式。
- 第二批回滚时，恢复模块私有表单、lookup 和 summary block 渲染，保留第一批列表/详情 Host 不受影响。

## 12. 验证方案

设计文档验证：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-12-frontend-standard-crud-platformization-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

第一批实施验证建议：

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
```

第一批残留扫描建议：

```powershell
rg -n "StandardCrudListResult|StandardCrudDetail|normalizeSummaryBlock|normalizePersonListResponse|normalizePersonDetail|getPersonListRuntimeSchema|getPersonDetailRuntimeSchema" frontend/modules/organization/src frontend/packages
```

第二批实施验证建议：

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-modules/organization build
```

第二批残留扫描建议：

```powershell
rg -n "getCompanies|getDepartments|getPositions|employmentStatusOptions|PersonEditorForm|resolveListText|resolveDetailText|AuthorizationSummaryCard|PersonUserLinkCard" frontend/modules/organization/src frontend/packages
```

扫描不是要求这些名称全部消失，而是要求每个命中都能解释为人员专属语义或第二批明确保留项。

## 13. 未决问题

当前没有阻塞第一批设计和计划拆分的未决问题。

实施计划中需要做命名确认，但不改变设计边界：

- 标准 CRUD 前端客户端放在 `frontend/packages/sdk/src/standardCrud.ts`，还是拆成 `standardCrud/types.ts` 与 `standardCrud/client.ts`。
- 标准详情入口使用新增 `standard-detail` page entry，还是先由现有 route 包装 `StandardCrudDetailPageHost`。
- 第一批人员 create / edit 是继续使用模块 Drawer，还是先提供最小 action handler 包装。

这些点属于实施组织方式，不影响“两批推进”和平台/模块边界。

## 14. 实施拆分建议

### 14.1 第一批：标准 CRUD 页面主流程平台化

第一批目标：让人员中心不再自带标准 CRUD 列表/详情主流程样板。

最小交付：

- SDK 导出标准 CRUD 返回模型、客户端帮助器和资源注册契约。
- Shell 提供通用 list/detail schema resolver。
- Shell 提供标准列表 Host，替代人员中心中通用查询、加载、表格、分页、错误和空态流程。
- Shell 提供标准详情 Host，替代人员中心中通用按 id 加载、详情错误态和标准 page actions。
- Organization 作为试点接入新注册契约。
- 保留人员专属表单、账号绑定、授权摘要和复杂 lookup 到第二批。

第一批验收条件：

- `PersonListStandardListEntry.tsx` 中不再持有完整标准列表状态机。
- `PersonDetailPage.tsx` 中不再持有完整标准详情加载状态机，或已成为薄包装。
- 标准 CRUD 类型不再定义在 Organization 私有 service 中。
- 人员账号绑定和授权摘要仍在 Organization 模块内。
- 验证记录必须写明“第二批未完成项”，并引用本文第 14.2 节。
- `docs/phases/current-handoff.md` 在第一批收口时必须把第二批列为推荐下一步或明确说明为何暂缓。

### 14.2 第二批：表单、数据源、动作和摘要深层平台化

第二批目标：消除人员中心里剩余的可复制表单、lookup、动作和摘要块通用逻辑。

固定交付清单：

- 通用字段渲染器：支持文本、布尔、枚举、引用、日期和只读展示。
- 通用编辑表单：支持 create / edit / view、sections、必填、只读、默认值和提交值规范化。
- 通用数据源 lookup：支持 enum、dictionary、value list、model lookup 和显式级联依赖。
- 通用动作扩展：支持标准动作、模块动作、确认文案、pending 状态、错误提示和权限过滤。
- 通用 summary block 渲染协议：平台处理状态，模块只提供专属内容 renderer。
- 通用 i18n key 解析：统一 `dicKey + localKey` 规则。
- Organization 人员中心继续收缩，只保留人员账号绑定、授权摘要、组织归属特殊显示和必要业务校验。

第二批验收条件：

- `PersonEditorForm` 的通用字段和 lookup 流程已被平台组件承接，文件要么删除，要么只保留人员专属包装。
- `getCompanies / getDepartments / getPositions` 不再作为人员表单通用字段渲染的唯一入口；组织语义仍可由 Organization lookup provider 承接。
- `resolveListText / resolveDetailText` 这类 dicKey 拼接逻辑不再散落在人员页面组件中。
- `AuthorizationSummaryCard` 和 `PersonUserLinkCard` 只承载专属展示，不负责平台 summary 状态协议。
- 第二批验证记录必须扫描第一批遗留项并解释保留原因。

### 14.3 批间防遗忘机制

第一批实施计划必须包含一个“登记第二批遗留项”任务，至少更新以下位置之一：

- `docs/verification/organization-module-verification.md`
- 本次第一批专属验证记录
- `docs/phases/current-handoff.md`

登记内容必须包含：

- 第二批固定交付清单。
- 第一批实际暂留在 Organization 模块内的文件和原因。
- 第二批建议入口文档：本文档第 14.2 节。
- 第二批不做时的风险说明。

如果第一批完成后没有以上登记，应视为第一批未完成。

## 15. AI-First 最小检查清单

- 入口在哪里：设计入口是 `docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md`；第一批实施入口从 `frontend/packages/sdk/src/`、`frontend/packages/shell/src/pages/`、`frontend/packages/shell/src/metadata/` 和 `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx` 开始。
- 边界在哪里：平台抽标准 CRUD 前端主流程，Organization 保留人员账号绑定、授权摘要、组织归属业务语义和第二批前暂留的表单/lookup 组件；不修改后端、部署、CI 和 Shell 布局。
- 如何验证：设计阶段运行 `validate-ai-first-design.ps1` 与 `check-ai-first-design-targets.ps1`；实施阶段按第 12 节运行 SDK、UI、Shell、Organization 构建和 Shell 测试。
- 失败信号是什么：Host 出现人员业务硬编码、Organization 继续复制完整标准 CRUD 页面流程、第一批收口未登记第二批、metadata 承载执行算法、人员中心列表/详情行为回归，均视为失败信号。
- 如何回滚：按批次回滚。第一批恢复 Organization 原列表/详情入口并撤回新增 Host 接入；第二批恢复模块私有表单、lookup、动作和摘要渲染，不影响第一批已稳定的标准列表/详情 Host。
