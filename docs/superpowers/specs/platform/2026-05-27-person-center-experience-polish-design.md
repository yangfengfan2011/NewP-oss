# 人员中心体验完善详细设计

## 1. 背景

人员中心已经作为标准 CRUD 的核心样板完成了列表、详情、抽屉表单、停用启用、删除保护、lookup 和 summary 等能力接入。当前剩余问题主要集中在日常维护体验，而不是新的业务域能力：

- 行操作没有按人员启用状态收敛，启用和停用会同时出现。
- 后端返回的业务错误没有稳定透传到前端提示，用户只能看到较泛的失败文案。
- 列较多时，操作列跟随表格横向滚动，用户必须滚动到末尾才能执行查看、编辑、停用等动作。
- 操作列宽度尚未按“最多展示 3 个常用操作”的约束收敛，容易浪费横向空间。
- 删除等危险动作不应与常用动作平铺在同一视觉层级，应进入“更多”操作中，并继续保持确认流程。

本轮以“人员中心体验完善包 A”为目标，只处理这些低耦合、可验证、可回滚的体验问题。高级筛选、权限授权中心、用户角色模型和 `PersonNo` 数据库唯一索引不纳入本轮。

## 2. 目标

- 人员列表行操作按当前行状态展示：启用人员只展示“停用”，停用人员只展示“启用”。
- 人员列表操作列固定在右侧，列多或横向滚动时仍能直接操作当前行。
- 操作列按最多 3 个常用操作设计宽度，避免为更多潜在动作无限扩宽。
- 删除等危险动作统一收纳到“更多”中，避免与查看、编辑、停用、启用等常用动作平铺。
- Organization 前端服务能够优先展示后端业务错误消息，例如人员已绑定账号时删除失败的具体原因。
- 补齐人员中心样板文档，说明 metadata、Shell Host、Organization 资源声明和错误处理的入口与边界。

## 3. 非目标

- 不做高级筛选调整；公司、部门、岗位维护模块未完成前，不新增依赖这些实体的筛选体验。
- 不做权限设计或动作授权中心；本轮不引入用户、角色、授权配置或动作级权限管理。
- 不新增 `PersonNo` 数据库唯一索引；并发强一致性和历史数据清理另起设计处理。
- 不改变人员中心后端 CRUD 主流程、DTO 契约、路由或删除保护业务规则。
- 不把人员中心的业务动作规则泛化成所有 CRUD 资源的强制默认行为。
- 不引入新的表达式 DSL，不让元数据承载执行流程、分支算法或回滚逻辑。

## 4. 范围

### 4.1 允许修改的文件

- `modules/Organization/metadata/views/PersonList.view.json`
  - 为人员列表行动作补充静态、可校验的状态可见性声明。
- `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`
  - 解析并校验 `visibleWhen`，保持 metadata 输入受控、静态、可校验。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - 在标准 CRUD 列表 Host 中解释行状态可见性。
  - 固定操作列到右侧。
  - 按最多 3 个常用操作控制操作列宽度。
  - 将危险动作归入“更多”入口。
- `frontend/packages/shell/src/index.css`
  - 收敛操作列按钮布局和更多菜单样式，避免固定列内换行抖动。
- `frontend/modules/organization/src/services/organizationService.ts`
  - 解析非 2xx 响应体，并优先抛出后端业务错误消息。
- `frontend/packages/shell/src/__tests__/`
  - 增加或调整人员中心标准 CRUD 列表测试，覆盖状态动作、操作列、更多菜单和错误提示。
- `docs/modules/` 或相邻人员中心样板说明文档
  - 补充人员中心样板入口、边界、验证和回滚说明。

### 4.2 禁止触碰的范围

- `server/modules/Organization` 的业务规则与 API 契约。
- 数据库迁移、索引、种子数据和历史数据清理。
- 认证、登录、用户、角色、授权中心和菜单权限模型。
- 通用 Shell 路由模型、模块注册模型和非人员中心资源的行为。

## 5. 设计约束

- 元数据只声明“这个动作适用于什么静态行状态”和“这个动作是否危险”，不声明怎么排序、怎么折叠、怎么执行、怎么回滚。
- Shell Standard CRUD Host 负责把动作声明、行数据、权限结果和 UI 约束投影成最终操作列。
- Organization 模块只维护人员资源声明和服务调用，不拥有通用表格列固定、更多菜单或危险动作分组规则。
- 危险动作进入“更多”后，仍必须保留确认与错误提示，不允许因为被折叠而弱化安全边界。
- 操作列宽度应使用稳定常量或集中计算，避免每个资源私自猜宽度。
- 本轮所有规则应能通过单元测试或组件测试验证，不依赖人工截图口径作为唯一验收依据。

## 6. 总体方案

采用“人员中心 metadata 小声明 + Shell Host 统一投影 + Organization 服务错误透传”的方案。

人员中心 metadata 增加最小状态声明，例如 `disable` 仅在 `IsEnabled = true` 时可见，`enable` 仅在 `IsEnabled = false` 时可见。该声明表达的是动作适用状态，不表达动作执行流程。

Shell 的 `StandardCrudListPageHost` 在构建表格列时做四件事：

1. 先按已有权限结果过滤动作。
2. 再按行状态过滤动作。
3. 将危险动作放入“更多”菜单。
4. 将最多 3 个非危险常用动作平铺，操作列固定右侧并使用适合 3 个操作的宽度。

Organization 的 `requestJson` 对错误响应做结构化解析，优先使用后端返回的 `message`、`error.message` 或标准 CRUD 错误消息；无法解析时再使用现有兜底文案。

不选择的方案：

- 不在人员中心模块内硬编码表格操作列规则，因为这会把平台标准 CRUD 体验拆散到单模块。
- 不引入完整动作表达式系统，因为当前只需要稳定、静态、可校验的状态条件。
- 不把删除从更多菜单中完全移除，因为后端已有删除保护，本轮目标是降低误触并保留可发现性。

## 7. 关键模型

### 7.1 行动作状态声明

状态声明只支持受控字段、受控操作符和值比较，第一版只覆盖布尔字段等值判断。

```ts
interface StandardCrudRowActionVisibility {
  field: string;
  operator: 'equals';
  value: boolean | string | number | null;
}
```

人员中心预期声明：

```json
{
  "standardAction": "disable",
  "visibleWhen": {
    "field": "IsEnabled",
    "operator": "equals",
    "value": true
  }
}
```

```json
{
  "standardAction": "enable",
  "visibleWhen": {
    "field": "IsEnabled",
    "operator": "equals",
    "value": false
  }
}
```

如果没有 `visibleWhen`，动作沿用现有行为。这样其他资源不会因为本轮变更被迫迁移。

### 7.2 操作分组模型

```ts
interface RowActionProjection {
  inlineActions: StandardCrudRowAction[];
  overflowActions: StandardCrudRowAction[];
}
```

投影规则：

- `view` 不进入列表行内操作，保持当前列表 Host 的既有处理。
- 非危险动作优先进入 `inlineActions`。
- `delete` 等危险动作进入 `overflowActions`。
- 如果未来非危险动作超过 3 个，超过部分进入 `overflowActions`，但本轮人员中心按最多 3 个常用动作验收。

### 7.3 操作列模型

```ts
const STANDARD_CRUD_INLINE_ACTION_LIMIT = 3;
const STANDARD_CRUD_ACTION_COLUMN_WIDTH = 176;
```

列宽可以在实施时按实际按钮间距微调，但必须满足：

- 能容纳最多 3 个稳定展示位；当存在“更多”入口时，“更多”占用其中 1 个展示位。
- 不因只有 1 个或 2 个操作而动态收缩导致表格布局跳动。
- 固定在右侧，横向滚动时保持可见。

## 8. 核心流程

### 8.1 行操作渲染流程

1. `StandardCrudListPageHost` 读取 `schema.rowActions`。
2. 过滤当前用户无权动作，沿用现有权限判断结果。
3. 对每一行按 `visibleWhen` 判断动作是否适用。
4. 排除列表中不平铺的 `view` 动作。
5. 将危险动作放入更多菜单。
6. 将最多 3 个非危险动作平铺在操作列。
7. 表格操作列固定在右侧。

### 8.2 删除失败提示流程

1. 用户从“更多”中点击删除。
2. Shell 继续展示删除确认。
3. 用户确认后调用 Organization 删除接口。
4. 后端返回业务错误，例如“当前人员已绑定账号，不能删除。”
5. `organizationService.requestJson` 解析响应体并抛出具体消息。
6. Shell 现有错误展示逻辑显示具体业务消息。

### 8.3 启用停用流程

1. 已启用人员行只展示“停用”。
2. 已停用人员行只展示“启用”。
3. 执行动作成功后刷新列表。
4. 刷新后的行状态重新投影动作，避免旧状态动作残留。

## 9. 接口与集成点

- Metadata authoring 入口：`modules/Organization/metadata/views/PersonList.view.json`。
- Shell Host 入口：`frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`。
- Organization 服务入口：`frontend/modules/organization/src/services/organizationService.ts`。
- 测试入口：`frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx` 及相邻列表布局测试。
- 文档入口：人员中心样板说明文档，引用本规格并说明实际文件职责。

本轮不新增后端接口，也不调整 standard CRUD SDK 的公开 API。若实施时发现类型需要扩展，应优先在 Shell 内部定义最小兼容类型；只有多个资源明确复用后，才考虑提升到 SDK 契约。

## 10. 数据与状态

- 行状态来源于列表接口返回的当前行数据，人员中心使用 `IsEnabled`。
- 动作声明来源于 runtime metadata 视图定义。
- 危险动作归类优先使用已有 `standardAction` 语义，第一版将 `delete` 视为危险动作。
- 错误消息来源于后端响应体；前端不持久化错误，只在当前交互中提示。
- 操作列固定和宽度属于 UI 投影状态，不进入 metadata。

## 11. 错误处理与风险

- 风险：后端错误响应格式不完全一致。
  - 控制：按 `message`、`error.message`、标准 CRUD 错误消息和文本响应逐级兜底。
- 风险：`visibleWhen` 字段名与列表数据字段大小写不一致。
  - 控制：人员中心只使用当前列表真实字段 `IsEnabled`，测试覆盖启用和停用两种行数据。
- 风险：危险动作进入更多后，测试选择器或用户习惯变化。
  - 控制：保留动作 label 和确认文案，只改变承载位置；测试覆盖更多菜单中存在删除动作。
- 风险：固定右侧操作列影响窄屏布局。
  - 控制：列宽固定为 3 操作适配宽度，窄屏仍依赖表格横向滚动，操作列保持可见。
- 回滚：移除 `visibleWhen` 声明、恢复 Host 操作列投影逻辑、恢复 `requestJson` 旧错误处理，即可回到当前行为。

## 12. 验证方案

最小验证命令：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout
pnpm --dir frontend --filter @icp-platform/shell test -- organization-service-error-handling
pnpm --dir frontend --filter @icp-modules/organization build
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-27-person-center-experience-polish-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

行为验收：

- 启用人员行展示停用，不展示启用。
- 停用人员行展示启用，不展示停用。
- 删除动作位于更多菜单内，并保留确认。
- 操作列固定右侧，列多时无需滚到末尾才能操作。
- 操作列宽度适合最多 3 个常用操作，不出现明显空白或挤压。
- 删除已绑定账号人员时，前端展示后端业务消息。

## 13. 未决问题

- 操作列宽度的最终像素值允许在实施时根据 Ant Design 表格按钮间距微调，但必须围绕 3 个常用操作宽度收敛。
- 若未来新增动作超过 3 个，是否所有非危险溢出动作都进入更多菜单，本轮只定义人员中心和当前 Host 的兼容行为。

## 14. 实施拆分建议

1. 先补测试：覆盖启用停用互斥、危险动作进入更多、操作列固定和错误消息透传。
2. 再实现 Shell 行动作投影和操作列固定。
3. 再更新人员中心 metadata 的 `visibleWhen`。
4. 再实现 Organization 服务错误解析。
5. 最后补人员中心样板说明文档并跑设计与前端定向验证。

## 15. AI-First 最小检查清单

- 入口在哪里：从 `StandardCrudListPageHost.tsx` 的行动作投影、`PersonList.view.json` 的行动作声明、`organizationService.ts` 的 `requestJson` 进入。
- 边界在哪里：只处理人员中心列表体验和 Organization 前端错误透传；不进入权限、用户角色、数据库索引、高级筛选和后端业务主流程。
- 如何验证：执行本设计第 12 节命令，并用测试断言行为验收项。
- 失败信号是什么：启用停用同时出现、删除仍平铺在操作列、操作列不固定、删除绑定账号人员只显示泛化错误、设计校验脚本失败。
- 如何回滚：回退本轮修改的 metadata、Shell Host、Organization 服务和样板文档；无数据库迁移，无持久化数据变更。
