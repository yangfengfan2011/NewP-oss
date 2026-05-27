# 人员中心体验完善实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成人员中心包 A：行操作按状态展示、操作列固定右侧并按 3 个展示位收敛、危险动作进入更多、后端业务错误透传到前端。

**Architecture:** 人员中心 metadata 只声明动作适用状态；Shell Standard CRUD Host 负责行数据投影、危险动作分组、操作列固定和错误展示；Organization 前端服务负责把后端错误响应解析成用户可读异常。后端、权限中心、高级筛选和数据库索引不在本计划内。

**Tech Stack:** React 18、TypeScript、Vite、Vitest、Ant Design v5、pnpm workspace、.NET Organization 定向测试。

---

## 前置要求

- 设计入口：`docs/superpowers/specs/platform/2026-05-27-person-center-experience-polish-design.md`。
- 实施前执行 `git status --short`，确认工作区改动只包含本设计与计划，或先记录已有用户改动。
- 不切换分支；如果后续需要隔离分支，由当前工作区策略另行决定。
- 本计划只处理人员中心体验包 A，不实现授权中心、用户角色、`PersonNo` 数据库唯一索引和公司/部门/岗位高级筛选体验。
- 每个任务完成后运行任务内最小验证；最后运行完整验证命令。

## 文件结构

- 修改 `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`
  - 增加 `visibleWhen` 的受控 runtime schema。
  - 校验 `operator` 只允许 `equals`，字段引用必须存在于 model 字段集合。
- 修改 `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - 增加行状态判断 helper。
  - 增加危险动作判断与行动作投影 helper。
  - 将操作列固定在右侧，设置稳定宽度。
  - 保持删除确认、执行、错误展示和刷新逻辑不变。
- 修改 `frontend/packages/shell/src/index.css`
  - 收敛 `.np-row-actions` 和更多菜单样式，避免固定操作列内按钮换行抖动。
- 修改 `modules/Organization/metadata/views/PersonList.view.json`
  - 给 `disable` 和 `enable` 动作补充 `visibleWhen`。
- 修改 `frontend/modules/organization/src/services/organizationService.ts`
  - 在 `requestJson` 非 2xx 分支中解析错误响应体并优先使用业务消息。
- 修改 `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`
  - 覆盖 `visibleWhen` 解析、保留和校验。
- 修改 `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
  - 覆盖人员中心启用/停用互斥、危险动作进入更多、后端业务错误透传。
- 修改 `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
  - 覆盖操作列固定右侧、稳定宽度和 3 个展示位规则。
- 新增 `frontend/packages/shell/src/__tests__/organization-service-error-handling.test.ts`
  - 通过 Shell 的 Vitest 入口覆盖 Organization 服务错误解析。
- 修改 `docs/modules/organization-implementation.md`
  - 补充人员中心标准 CRUD 样板当前入口、边界、验证和回滚说明。

## Task 1：用失败测试锁定 metadata visibleWhen 契约

**Files:**
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`
- Modify: `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry`

- [ ] **Step 1: 写 schema 保留 visibleWhen 的失败测试**

在 `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts` 的 metadata-owned list schema 用例中，把 `disable` 动作扩展为：

```ts
{
  key: 'disable',
  labelKey: 'Action_Disable',
  standardAction: 'disable',
  variant: 'danger',
  confirmMessageKey: 'Action_Disable_Confirm',
  handlerKey: 'disable',
  visibleWhen: { field: 'IsEnabled', operator: 'equals', value: true },
}
```

并把断言改成：

```ts
expect(schema.rowActions[0]).toMatchObject({
  key: 'disable',
  confirmMessageKey: 'Action_Disable_Confirm',
  handlerKey: 'disable',
  visibleWhen: { field: 'IsEnabled', operator: 'equals', value: true },
});
```

- [ ] **Step 2: 写 unsupported visibleWhen operator 的失败测试**

在同一测试文件追加：

```ts
it('fails when row action visibleWhen uses an unsupported operator', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      descriptionKey: 'Description',
      mode: 'list',
      model: 'Person',
      columns: [{ key: 'isEnabled', labelKey: 'Column_IsEnabled', source: 'model', field: 'IsEnabled' }],
      rowActions: [{
        key: 'disable',
        labelKey: 'Action_Disable',
        standardAction: 'disable',
        handlerKey: 'disable',
        visibleWhen: { field: 'IsEnabled', operator: 'notEquals', value: true },
      }],
    },
    personRegistration,
    {
      modelDocuments: [{
        type: 'model',
        Person: { fields: [{ name: 'IsEnabled', storage: { type: 'bool' } }] },
      }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Description': '维护组织模块中的人员基础信息。',
        'PersonListView.Column_IsEnabled': '启用状态',
        'PersonListView.Action_Disable': '停用',
      },
    },
  )).toThrow('row action "disable" visibleWhen uses unsupported operator "notEquals"');
});
```

- [ ] **Step 3: 写 unknown visibleWhen field 的失败测试**

追加：

```ts
it('fails when row action visibleWhen references an unknown model field', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      descriptionKey: 'Description',
      mode: 'list',
      model: 'Person',
      columns: [{ key: 'isEnabled', labelKey: 'Column_IsEnabled', source: 'model', field: 'IsEnabled' }],
      rowActions: [{
        key: 'disable',
        labelKey: 'Action_Disable',
        standardAction: 'disable',
        handlerKey: 'disable',
        visibleWhen: { field: 'MissingState', operator: 'equals', value: true },
      }],
    },
    personRegistration,
    {
      modelDocuments: [{
        type: 'model',
        Person: { fields: [{ name: 'IsEnabled', storage: { type: 'bool' } }] },
      }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Description': '维护组织模块中的人员基础信息。',
        'PersonListView.Column_IsEnabled': '启用状态',
        'PersonListView.Action_Disable': '停用',
      },
    },
  )).toThrow('row action "disable" visibleWhen references unknown model field "MissingState"');
});
```

- [ ] **Step 4: 运行测试确认失败**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry
```

Expected: 至少 1 个断言失败，失败原因与 `visibleWhen` 未解析或未校验有关。

- [ ] **Step 5: 实现最小 schema 支持**

在 `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts` 增加类型：

```ts
export interface StandardCrudRowActionVisibilitySchema {
  field: string;
  operator: 'equals';
  value: boolean | string | number | null;
}
```

扩展 `StandardCrudRuntimeActionSchema`：

```ts
visibleWhen?: StandardCrudRowActionVisibilitySchema;
```

增加读取函数：

```ts
function readActionVisibility(value: unknown): StandardCrudRowActionVisibilitySchema | undefined {
  const record = readRecord(value);
  const field = readString(record.field);
  const operator = readString(record.operator);
  if (field.length === 0 && operator.length === 0 && !Object.prototype.hasOwnProperty.call(record, 'value')) {
    return undefined;
  }

  return {
    field,
    operator: operator as StandardCrudRowActionVisibilitySchema['operator'],
    value: typeof record.value === 'boolean' || typeof record.value === 'string' || typeof record.value === 'number' || record.value === null
      ? record.value
      : null,
  };
}
```

在 `readAction` 返回值中追加：

```ts
const visibleWhen = readActionVisibility(value.visibleWhen);
...
...(visibleWhen ? { visibleWhen } : {}),
```

增加校验函数并在 `buildStandardCrudListSchema` 的 model 字段校验段调用：

```ts
function assertActionVisibility(action: StandardCrudRuntimeActionSchema, modelFields: Set<string>): void {
  if (!action.visibleWhen) {
    return;
  }
  if (action.visibleWhen.operator !== 'equals') {
    throw new Error(`row action "${action.key}" visibleWhen uses unsupported operator "${action.visibleWhen.operator}"`);
  }
  assertModelFieldReference(
    action.visibleWhen.field,
    modelFields,
    `row action "${action.key}" visibleWhen references unknown model field "${action.visibleWhen.field}"`,
  );
}
```

调用位置：

```ts
for (const action of schema.rowActions) {
  assertActionVisibility(action, modelFields);
}
```

- [ ] **Step 6: 运行测试确认通过**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry
```

Expected: `standard-crud-schema-and-registry` 通过。

## Task 2：用失败测试锁定行操作投影与操作列约束

**Files:**
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/index.css`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout`

- [ ] **Step 1: 写启用/停用互斥与危险动作进更多的失败测试**

在 `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx` 追加：

```ts
it('按人员启用状态展示启用停用，并将危险动作收纳到更多', async () => {
  vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
    items: [
      { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
      { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: false },
    ],
    total: 2,
    page: 1,
    pageSize: 20,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = renderHost(container);
  });
  await flushAsync();

  const enabledRow = findTableRowByText(container, '张三');
  const disabledRow = findTableRowByText(container, '李四');
  expect(enabledRow?.textContent).toContain('编辑');
  expect(enabledRow?.textContent).toContain('更多');
  expect(enabledRow?.textContent).not.toContain('停用');
  expect(enabledRow?.textContent).not.toContain('启用');
  expect(disabledRow?.textContent).toContain('编辑');
  expect(disabledRow?.textContent).toContain('启用');
  expect(disabledRow?.textContent).toContain('更多');
  expect(disabledRow?.textContent).not.toContain('停用');

  await act(async () => {
    findAntdButtonByText(enabledRow ?? container, '更多')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
  await flushAsync();
  expect(document.body.textContent).toContain('删除');
  expect(document.body.textContent).toContain('停用');
  expect(document.body.textContent).not.toContain('启用');
});
```

- [ ] **Step 2: 调整现有布局测试期望**

在 `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx` 的列表布局用例中，把操作列断言调整为：

```ts
const firstActionCell = bodyRows[0]?.querySelector('td:last-child');
expect(firstActionCell?.className).toContain('ant-table-cell-fix-right');
expect(firstActionCell?.textContent).not.toContain('查看');
expect(firstActionCell?.textContent).toContain('编辑');
expect(firstActionCell?.textContent).toContain('更多');
expect(firstActionCell?.textContent).not.toContain('删除');
expect(firstActionCell?.textContent).not.toContain('停用');
expect(firstActionCell?.textContent).not.toContain('启用');
expect(firstActionCell?.querySelectorAll('.np-row-action-button')).toHaveLength(1);
expect(firstActionCell?.querySelector('.np-row-actions-more')).not.toBeNull();
```

并追加列宽断言：

```ts
const actionHeader = container.querySelector('th:last-child');
expect(actionHeader?.className).toContain('ant-table-cell-fix-right');
expect(actionHeader?.getAttribute('style') ?? '').toContain('right: 0px');
```

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout
```

Expected: 行动作仍未按 `visibleWhen` 过滤，危险动作仍可能平铺或操作列未 fixed，测试失败。

- [ ] **Step 4: 实现 Host 行动作投影 helper**

在 `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx` 常量区替换：

```ts
const MAX_ROW_ACTION_BUTTONS = 3;
```

为：

```ts
const STANDARD_CRUD_INLINE_ACTION_LIMIT = 3;
const STANDARD_CRUD_ACTION_COLUMN_WIDTH = 176;
```

增加 helper：

```ts
function readRowActionValue(row: Record<string, unknown>, field: string): unknown {
  return row[field];
}

function isRowActionVisibleForRow(action: { visibleWhen?: { field: string; operator: 'equals'; value: unknown } }, row: Record<string, unknown>): boolean {
  if (!action.visibleWhen) {
    return true;
  }
  if (action.visibleWhen.operator !== 'equals') {
    return false;
  }
  return readRowActionValue(row, action.visibleWhen.field) === action.visibleWhen.value;
}

function isDangerRowAction(action: { standardAction?: string; variant?: string }): boolean {
  return action.standardAction === 'delete' || action.variant === 'danger';
}
```

调整 `RowActionButtonGroup` 分组逻辑：

```ts
function RowActionButtonGroup({ actions }: { actions: RowActionItem[] }) {
  const inlineCandidates = actions.filter((action) => !action.danger);
  const dangerActions = actions.filter((action) => action.danger);
  const hasOverflow = dangerActions.length > 0 || inlineCandidates.length > STANDARD_CRUD_INLINE_ACTION_LIMIT;
  const inlineLimit = hasOverflow ? STANDARD_CRUD_INLINE_ACTION_LIMIT - 1 : STANDARD_CRUD_INLINE_ACTION_LIMIT;
  const inlineActions = inlineCandidates.slice(0, inlineLimit);
  const overflowActions = [
    ...inlineCandidates.slice(inlineLimit),
    ...dangerActions,
  ];
```

- [ ] **Step 5: 在 row action 构建链路应用 visibleWhen 与 danger**

在 `rowActions` 构造链路中，将过滤链调整为：

```ts
const rowActions: RowActionItem[] = rowActionDefinitions
  .filter((action) => hasActionPermission(action, grantedPermissions))
  .filter((action) => isRowActionVisibleForRow(action, row as Record<string, unknown>))
  .filter((action) => action.standardAction !== 'view')
  .map((action): RowActionItem => {
```

删除动作和普通动作的 `danger` 赋值保持一致：

```ts
danger: isDangerRowAction(action),
```

删除动作仍保留 `confirmMessage`、`setDeletingActionKey`、`context.refresh()` 和 `closeDrawerForRow(rowId)` 的现有流程。

- [ ] **Step 6: 固定操作列并设置宽度**

在操作列定义中设置：

```ts
{
  key: 'actions',
  dataIndex: 'actions',
  title: '操作',
  width: STANDARD_CRUD_ACTION_COLUMN_WIDTH,
  fixed: 'right' as const,
  sorter: false,
  render: (row: TRow) => {
```

在 columns map 中透传 `fixed`：

```ts
fixed: column.fixed,
```

在 metadata columns 映射处补固定列字段，保持列对象 shape 稳定：

```ts
fixed: undefined,
```

- [ ] **Step 7: 收敛 CSS 防止操作列换行抖动**

把 `frontend/packages/shell/src/index.css` 中 `.np-row-actions` 调整为：

```css
.np-row-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: nowrap;
  white-space: nowrap;
}
```

更多菜单保留纵向：

```css
.np-row-actions-overflow-menu {
  display: grid;
  min-width: 88px;
  gap: 2px;
}
```

- [ ] **Step 8: 运行测试确认通过**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout
```

Expected: 两个测试文件通过。

## Task 3：更新人员中心 metadata 状态声明

**Files:**
- Modify: `modules/Organization/metadata/views/PersonList.view.json`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry person-center-crud-pro-sample`

- [ ] **Step 1: 给 disable 增加 IsEnabled=true 条件**

在 `modules/Organization/metadata/views/PersonList.view.json` 的 `disable` 动作中加入：

```json
"visibleWhen": {
  "field": "IsEnabled",
  "operator": "equals",
  "value": true
}
```

目标片段：

```json
{
  "key": "disable",
  "labelKey": "Action_Disable",
  "standardAction": "disable",
  "variant": "danger",
  "confirmMessageKey": "Action_Disable_Confirm",
  "handlerKey": "disable",
  "visibleWhen": {
    "field": "IsEnabled",
    "operator": "equals",
    "value": true
  }
}
```

- [ ] **Step 2: 给 enable 增加 IsEnabled=false 条件**

在 `enable` 动作中加入：

```json
"visibleWhen": {
  "field": "IsEnabled",
  "operator": "equals",
  "value": false
}
```

目标片段：

```json
{
  "key": "enable",
  "labelKey": "Action_Enable",
  "standardAction": "enable",
  "confirmMessageKey": "Action_Enable_Confirm",
  "handlerKey": "enable",
  "visibleWhen": {
    "field": "IsEnabled",
    "operator": "equals",
    "value": false
  }
}
```

- [ ] **Step 3: 运行 schema 与人员中心测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry person-center-crud-pro-sample
```

Expected: schema resolver 能解析 metadata 中的 `visibleWhen`，人员中心样板测试通过。

## Task 4：实现后端业务错误透传

**Files:**
- Create: `frontend/packages/shell/src/__tests__/organization-service-error-handling.test.ts`
- Modify: `frontend/modules/organization/src/services/organizationService.ts`
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
- Verify:
  - `pnpm --dir frontend --filter @icp-platform/shell test -- organization-service-error-handling`
  - `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`
  - `pnpm --dir frontend --filter @icp-modules/organization build`

- [ ] **Step 1: 新增 Organization 服务错误解析测试**

创建 `frontend/packages/shell/src/__tests__/organization-service-error-handling.test.ts`：

```ts
import { afterEach, describe, expect, it, vi } from 'vitest';
import { deletePerson } from '../../../../modules/organization/src/services/organizationService';

describe('organizationService error handling', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('throws backend business message from standard crud error response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      code: 'Person.LinkedUser',
      message: '当前人员已绑定账号，不能删除。',
    }), { status: 400, headers: { 'Content-Type': 'application/json' } }));

    await expect(deletePerson('person-1')).rejects.toThrow('当前人员已绑定账号，不能删除。');
  });

  it('falls back to caller message when error body has no readable message', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      code: 'Unknown',
    }), { status: 500, headers: { 'Content-Type': 'application/json' } }));

    await expect(deletePerson('person-1')).rejects.toThrow('删除人员失败');
  });
});
```

- [ ] **Step 2: 在 Shell 人员中心样板中补透传行为测试**

在 `person-center-crud-pro-sample.test.tsx` 删除失败用例旁追加：

```ts
it('删除失败时展示后端透传的业务错误', async () => {
  const resource = organizationModule.standardCrudResources?.[0];
  if (!resource) {
    throw new Error('未找到人员标准 CRUD 资源。');
  }

  const deleteHandler = vi.fn(async () => {
    throw new Error('当前人员已绑定账号，不能删除。');
  });
  const sampleModule = {
    ...organizationModule,
    standardCrudResources: [{
      ...resource,
      actionHandlers: {
        ...(resource.actionHandlers ?? {}),
        delete: deleteHandler,
      },
    }],
  };
  vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
    items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
    total: 1,
    page: 1,
    pageSize: 20,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = renderHost(container);
  });
  await flushAsync();

  const deleteButton = await findOverflowRowActionButton(container, '张三', '删除');
  expect(deleteButton).toBeDefined();
  await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
  await flushAsync();
  await clickPopconfirmButton('确认');
  await flushAsync();

  expect(container.textContent).toContain('当前人员已绑定账号，不能删除。');
});
```

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- organization-service-error-handling
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-modules/organization build
```

Expected: `organization-service-error-handling` 因 `requestJson` 尚未解析后端消息而失败；人员中心样板测试继续通过或只在错误消息透传断言处失败；Organization build 通过。

- [ ] **Step 4: 实现错误响应解析 helper**

在 `frontend/modules/organization/src/services/organizationService.ts` 的 `requestJson` 前增加：

```ts
function readErrorMessage(value: unknown): string | undefined {
  if (typeof value === 'string' && value.trim().length > 0) {
    return value;
  }
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return undefined;
  }

  const record = value as Record<string, unknown>;
  if (typeof record.message === 'string' && record.message.trim().length > 0) {
    return record.message;
  }

  const nestedError = record.error;
  if (nestedError && typeof nestedError === 'object' && !Array.isArray(nestedError)) {
    const nestedMessage = (nestedError as Record<string, unknown>).message;
    if (typeof nestedMessage === 'string' && nestedMessage.trim().length > 0) {
      return nestedMessage;
    }
  }

  return undefined;
}

async function readResponseErrorMessage(response: Response, fallback: string): Promise<string> {
  const contentType = response.headers.get('content-type') ?? '';
  try {
    if (contentType.includes('application/json')) {
      return readErrorMessage(await response.json()) ?? fallback;
    }

    const text = await response.text();
    return readErrorMessage(text) ?? fallback;
  } catch {
    return fallback;
  }
}
```

调整 `requestJson`：

```ts
if (!response.ok) {
  throw new Error(await readResponseErrorMessage(response, errorMessage));
}
```

- [ ] **Step 5: 运行验证**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- organization-service-error-handling
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-modules/organization build
```

Expected: 样板测试通过，Organization TypeScript build 通过。

## Task 5：补人员中心样板文档

**Files:**
- Modify: `docs/modules/organization-implementation.md`
- Verify: 文档人工检查加 `rg -n "人员中心标准 CRUD 样板|visibleWhen|操作列|错误透传" docs/modules/organization-implementation.md`

- [ ] **Step 1: 补文档章节**

在 `docs/modules/organization-implementation.md` 的“前端实现”后追加：

```md
## 人员中心标准 CRUD 样板

人员中心当前作为标准 CRUD 样板维护，入口分为四层：

- `modules/Organization/metadata/views/PersonList.view.json`：声明查询、列表列、页面动作、行动作和行动作状态条件。metadata 只描述“动作适用于什么状态”，例如 `disable` 只适用于 `IsEnabled=true`，不描述执行流程。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`：负责列表状态机、操作列固定、行动作投影、危险动作更多菜单、抽屉打开和错误展示。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`：保留人员资源的 provider、mapper、renderer 和 action handler，不承载 Shell 通用页面规则。
- `frontend/modules/organization/src/services/organizationService.ts`：负责 Organization API 调用和后端业务错误透传。

当前边界：

- 不在本样板中维护用户、角色、授权中心或动作级授权配置。
- 不在本样板中新增 `PersonNo` 数据库唯一索引。
- 不在公司、部门、岗位维护模块完成前继续扩展高级筛选体验。

验证入口：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout
pnpm --dir frontend --filter @icp-modules/organization build
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

失败信号包括：启用和停用同时出现在同一行、危险动作平铺在操作列、操作列未固定右侧、删除绑定账号人员只展示泛化错误。

回滚方式：回退 `PersonList.view.json` 的 `visibleWhen`、Shell Host 行动作投影、Organization 服务错误解析和本章节文档；本轮不涉及数据库迁移或后端契约变更。
```

- [ ] **Step 2: 检查文档关键字**

Run:

```powershell
rg -n "人员中心标准 CRUD 样板|visibleWhen|操作列|错误透传" docs/modules/organization-implementation.md
```

Expected: 输出新增章节中的关键行。

## Task 6：整体验证与收口

**Files:**
- 仅验证；如果前面任务暴露小范围问题，只修正本计划列出的文件。

- [ ] **Step 1: 运行前端定向测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry person-center-crud-pro-sample organization-person-center-list-layout
pnpm --dir frontend --filter @icp-platform/shell test -- organization-service-error-handling
```

Expected: 全部通过；允许出现既有 AntD `findDOMNode` 或 React Router future warning，但不能有 failed test。

- [ ] **Step 2: 运行 Organization 前端构建**

Run:

```powershell
pnpm --dir frontend --filter @icp-modules/organization build
```

Expected: TypeScript 编译通过。

- [ ] **Step 3: 运行后端 Organization 定向测试**

Run:

```powershell
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected: 测试通过。若遇到 .NET build server 文件锁，先运行 `dotnet build-server shutdown`，再重跑同一命令。

- [ ] **Step 4: 运行设计文档校验**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-27-person-center-experience-polish-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

Expected: 两个脚本都通过。

- [ ] **Step 5: 检查工作区 diff**

Run:

```powershell
git status --short
git diff --stat
```

Expected: diff 只包含本计划列出的文件，以及 `docs/architecture/ai-first-design-check-targets.txt` 中设计登记项。若出现无关文件，先确认来源，不回退用户改动。

- [ ] **Step 6: 准备最终汇报**

最终汇报必须包含：

- 改了什么：状态动作、操作列、危险动作更多、错误透传、文档。
- 影响文件或模块：Shell Host、Shell tests、Organization metadata、Organization service、docs。
- 验证结果：列出实际执行命令和通过状态。
- 已知风险：操作列宽度如需像素微调、旧测试选择器变化。
- 回滚思路：按本计划 Task 5 文档中的回滚方式说明。
