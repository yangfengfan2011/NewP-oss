# Standard CRUD 通用 XLSX 导出与抽屉 URL 同步设计

## 1. 背景

人员中心的 `PersonList.view.json` 已声明 `export.modes`，但 Standard CRUD Host 只解析该字段，没有渲染入口或执行导出。与此同时，旧入口通过 `?drawer=create|view|edit&id=...` 打开抽屉后，关闭抽屉不会同步清理 URL，用户复制链接或使用浏览器前进后退时体验不够顺滑。

这两个问题都属于 Standard CRUD 平台能力，不应固化为人员中心特例。本设计在平台层补齐通用导出和抽屉 URL 同步，让后续标准 CRUD 资源通过元数据声明获得一致行为。

## 2. 目标

- 为 Standard CRUD 列表提供通用 `.xlsx` 导出能力。
- `export.modes` 继续只声明允许的导出模式，不复制模型、列或流程信息。
- 导出列的 authoring 真源是 view 文件下的 `columns`，前端运行时解析后的 `schema.columns` 只是消费结果。
- 导出文件由后端通用接口生成，前端只负责展示入口并提交当前视图列、筛选、分页和模式。
- 抽屉打开、关闭与浏览器 URL 查询参数保持同步，兼容旧的 `drawer/id` 链接。

## 3. 非目标

- 不做复杂 Excel 样式、合并单元格、公式、图片或多 sheet。
- 不新增人员中心专属导出代码。
- 不把 `model`、字段格式化流程或导出算法写入 `view.export`。
- 不在本轮实现高级筛选 lookup 化。
- 不改变 Standard CRUD 的权限模型；导出沿用列表接口所在资源的后端路由边界。

## 4. 范围

- 后端：`server/platform-crud` 的 Standard CRUD 路由、导出模型和 XLSX 写出 helper。
- 前端 SDK：Standard CRUD client 增加通用导出调用。
- 前端 Shell：`StandardCrudListPageHost` 根据 `export.modes` 渲染导出入口，并同步抽屉 URL。
- 测试：后端路由测试、前端 Host 测试、必要的 SDK/helper 单测。

不修改具体模块数据库、组织模块服务、部署脚本或 CI。

## 5. 设计约束

- Host 只负责组合、状态和受控注册，不写模块私有逻辑。
- 元数据只声明“允许什么”，导出执行流程必须收口在代码内核。
- 后端不能盲信前端传入字段；导出列必须被 descriptor `FieldSet.ListFields` 白名单约束。
- `view.columns` 是导出列顺序和标题来源；前端传给后端的列定义只是一份运行时请求，不是长期 authoring 真源。
- `.xlsx` 第一版由平台内核生成最小 Open XML 工作簿，不新增第三方依赖；后续如果需要复杂样式，再以受控 helper 替换实现。

## 6. 总体方案

新增后端通用接口：

`POST /{standardCrudRoute}/export`

请求体包含：

- `mode`: `current` 或 `all`
- `columns`: 前端从 view columns 解析出的字段、标题和列 key
- `query`: 当前列表查询，包含筛选、分页和排序

后端流程：

1. 校验 `mode`。
2. 将 `columns.field` 与 descriptor `FieldSet.ListFields` 求交，只导出允许字段。
3. `current` 使用请求内分页；`all` 使用当前筛选和排序并把分页归一到第一页、足够大的 pageSize。
4. 调用现有 Standard CRUD service 获取列表结果。
5. 用最小 XLSX writer 生成单 sheet 工作簿。
6. 返回 `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` 和稳定文件名。

前端流程：

1. Host 读取 `schema.exportModes`。
2. 有 `current/all` 时在列表工具区渲染导出按钮。
3. 点击后用当前 `appliedFilters`、分页状态和 `schema.columns` 调用 SDK `exportList`。
4. 浏览器下载后端返回的 blob。

抽屉 URL 同步：

- 用户点击新建、查看、编辑时写入 `drawer` 和可选 `id`。
- 关闭抽屉时清理 `drawer/id`，保留其他查询参数。
- URL 变化仍由现有兼容 effect 投影为抽屉状态。

## 7. 关键模型

后端新增模型：

- `StandardCrudExportMode`: `Current` / `All`
- `StandardCrudExportColumn`: `Key`、`Field`、`Title`
- `StandardCrudExportRequest`: `Mode`、`Columns`、`Query`
- `StandardCrudExportFile`: `FileName`、`ContentType`、`Content`

前端 SDK 新增模型：

- `StandardCrudExportMode = 'current' | 'all'`
- `StandardCrudExportColumnRequest`
- `StandardCrudExportRequest`
- `StandardCrudClient.exportList(request)`

字段边界：

- `field` 必须来自 view column 解析结果。
- 后端只允许导出 descriptor list fields 内的字段。
- 标题使用前端已本地化标题，后端只负责写入文件。

## 8. 核心流程

导出当前页：

1. 用户点击“导出当前”。
2. 前端提交当前 `pageIndex/pageSize` 和已应用筛选。
3. 后端获取当前页列表。
4. 后端按列定义写出 `.xlsx`。

导出全部：

1. 用户点击“导出全部”。
2. 前端提交已应用筛选和排序，分页仍可带上但不作为最终页范围。
3. 后端把分页归一为 `pageIndex=1`、大 pageSize。
4. 后端按匹配结果写出 `.xlsx`。

抽屉 URL：

1. 用户点击打开抽屉时，Host 用 `navigate(..., { replace: false })` 写入 URL。
2. 用户关闭抽屉时，Host 用 `replace: true` 清理兼容参数。
3. 浏览器前进后退触发 `location.search` 变化，现有兼容 effect 重新投影抽屉状态。

## 9. 接口与集成点

后端集成点：

- `StandardCrudEndpointRouteBuilderExtensions.MapResource` 增加 `/export`。
- `BuildRouteCandidates` 纳入 `/export`，启动期继续检查派生路由冲突。
- 新 helper 位于 `server/platform-crud/Exports`，只依赖 BCL。

前端集成点：

- `frontend/packages/sdk/src/standardCrud.ts` 增加导出请求类型和 client 方法。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx` 组合导出按钮、请求列和 URL 同步。
- `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx` 如需导出当前页分页状态，可通过受控 props 回传分页变化；不读取 Ant Design 内部状态。

## 10. 数据与状态

- 导出不新增持久化数据。
- 前端导出状态只保留按钮 loading 和错误提示。
- 后端返回文件字节，生命周期局限于一次请求。
- `all` 导出第一版使用平台默认上限，避免误触发无限内存占用；达到上限时仍返回当前可导出的结果，后续可通过异步任务扩展。

## 11. 错误处理与风险

失败点：

- 前端传入非法 mode。
- 前端列字段不在后端白名单内。
- 后端列表查询失败。
- XLSX 生成失败。
- 浏览器下载 blob 失败。

控制方式：

- mode 非法返回 400。
- 字段不在白名单时忽略该列；若没有可导出列，返回 400。
- XLSX writer 有独立单测验证必要 zip entry 和 sheet 内容。
- 前端导出失败显示 message，不影响列表加载和抽屉操作。

## 12. 验证方案

- 后端定向测试：`dotnet test tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj --filter StandardCrud`
- 前端定向测试：`pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host.test.tsx person-center-crud-pro-sample.test.tsx`
- 前端构建：`pnpm --dir frontend --filter @icp-platform/shell build`
- 设计检查：
  - `powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-01-standard-crud-xlsx-export-and-drawer-url-design.md`
  - `powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`

## 13. 未决问题

- 第一版 `all` 导出上限取值采用平台常量，后续是否改为配置项由新的设计处理。
- 导出样式、列宽、日期/枚举格式化扩展点不在本轮处理。

## 14. 实施拆分建议

1. 后端新增导出模型、XLSX writer 和 `/export` 路由，先用测试锁定文件返回。
2. 前端 SDK 增加 `exportList`，Host 渲染通用导出入口。
3. Host 同步抽屉 URL，补充关闭和浏览器回退测试。
4. 跑设计检查、后端定向测试、前端定向测试和前端构建。

## 15. AI-First 最小检查清单

- 入口在哪里：后端从 `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs` 进入，前端从 `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx` 进入。
- 边界在哪里：只改 Standard CRUD 平台层、SDK client、Shell Host 和测试；不改人员中心私有服务、不改 `view.export` 结构。
- 如何验证：执行第 12 节列出的设计检查、后端测试、前端测试和前端构建。
- 失败信号是什么：`/export` 返回非 xlsx content type、非法字段被导出、导出按钮未按 `export.modes` 显示、关闭抽屉后 URL 仍残留 `drawer/id`，或相关测试失败。
- 如何回滚：删除新增导出模型/helper/路由和 SDK `exportList`，回退 Host 导出按钮与 URL 同步改动，保留原有 `export.modes` 解析不影响既有页面。
