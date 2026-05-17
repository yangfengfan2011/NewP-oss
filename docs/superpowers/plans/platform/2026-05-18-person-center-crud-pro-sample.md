# 人员中心后台 CRUD Pro 样板固化实施计划

> **给执行代理的要求：** 实施本计划时必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，并按任务逐项执行。步骤使用复选框 `- [ ]` 记录进度。

**目标：** 建立可复用的 Ant Design Pro 风格标准 CRUD 工作台，并让人员中心成为第一套完整的列表、详情抽屉、表单抽屉样板。

**架构：** 平台契约继续作为事实源。`frontend/packages/ui` 只提供无业务语义的 CRUD 视觉 adapter；`frontend/packages/shell` 负责编排 standard CRUD 状态、路由、权限、动作、请求、抽屉和错误；`frontend/modules/organization` 只声明人员资源字段、筛选、列、lookup、summary 和业务 handler。

**技术栈：** React 18、TypeScript、Vite、Vitest、Ant Design v5、Ant Design Pro Components、pnpm workspace。

---

## 文件结构

- 新建 `frontend/packages/ui/src/pro-adapters/CrudWorkspacePage.tsx`
  - 无业务语义的 CRUD 页面框架，承载标题、描述、主操作、筛选区和内容区。
- 新建 `frontend/packages/ui/src/pro-adapters/CrudSearchBar.tsx`
  - 无业务语义的 AntD 筛选条，承载字段区、搜索、重置和更多筛选入口。
- 新建 `frontend/packages/ui/src/pro-adapters/CrudTableSection.tsx`
  - 包装 `SearchTablePage`，统一列表标题、总数、刷新工具栏和表格内容。
- 新建 `frontend/packages/ui/src/pro-adapters/CrudDetailDrawer.tsx`
  - 右侧详情抽屉，使用 `DetailPage`、summary 插槽、加载/错误状态和动作区。
- 新建 `frontend/packages/ui/src/pro-adapters/CrudFormDrawer.tsx`
  - 右侧表单抽屉，使用 `StandardCrudForm` 调用方内容和固定底部保存/取消操作。
- 新建 `frontend/packages/ui/src/pro-adapters/CrudDangerAction.tsx`
  - 危险动作确认组件，使用 AntD `Popconfirm`，统一 loading 和错误边界入口。
- 修改 `frontend/packages/ui/src/index.ts`
  - 导出新增 CRUD Pro adapter。
- 修改 `frontend/packages/sdk/src/standardCrud.ts`
  - 标准动作增加 `view/delete`，`StandardCrudClient` 增加 `delete`，资源注册增加可选 `listRoutePath`。
- 修改 `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - 从列表 Host 升级为 CRUD 工作台编排器，承接抽屉状态、详情加载、表单保存、刷新、新建、查看、编辑、删除和可选动作。
- 修改 `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
  - 保留独立详情页作为深链接和回滚入口，并与抽屉详情共享字段、summary 和动作投影思路。
- 修改 `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
  - 将 `delete` 作为标准危险动作处理，`disable` 保持可选动作。
- 修改 `frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`
  - 增加旧新建/编辑 URL 到列表抽屉入口的兼容信息。
- 修改 `frontend/packages/shell/src/App.tsx`
  - 将 `/organization/persons/new` 重定向到 `/organization/persons?drawer=create`。
- 修改 `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
  - 将原生筛选控件替换为 AntD 控件；移除行操作里的直接页面跳转；通过标准动作声明接入平台样板。
- 新建 `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
  - 覆盖新建/查看/编辑抽屉、刷新、删除协议、可选停用动作和路由兼容。
- 修改既有测试：
  - `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
  - `frontend/packages/shell/src/__tests__/organization-person-center-detail-layout.test.tsx`
  - `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`
  - `frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx`

## Task 1：新增 UI CRUD Pro Adapter 组件

**文件：**
- 新建：`frontend/packages/ui/src/pro-adapters/CrudWorkspacePage.tsx`
- 新建：`frontend/packages/ui/src/pro-adapters/CrudSearchBar.tsx`
- 新建：`frontend/packages/ui/src/pro-adapters/CrudTableSection.tsx`
- 新建：`frontend/packages/ui/src/pro-adapters/CrudDetailDrawer.tsx`
- 新建：`frontend/packages/ui/src/pro-adapters/CrudFormDrawer.tsx`
- 新建：`frontend/packages/ui/src/pro-adapters/CrudDangerAction.tsx`
- 修改：`frontend/packages/ui/src/index.ts`
- 验证：`pnpm --dir frontend --filter @icp-platform/ui build`

- [ ] **Step 1：新建 `CrudWorkspacePage.tsx`**

写入：

```tsx
import type { ReactNode } from 'react';
import { Space, Typography } from 'antd';

const { Title, Paragraph } = Typography;

export interface CrudWorkspacePageProps {
  title: ReactNode;
  description?: ReactNode;
  primaryActions?: ReactNode;
  filterBar?: ReactNode;
  children: ReactNode;
}

export function CrudWorkspacePage({
  title,
  description,
  primaryActions,
  filterBar,
  children,
}: CrudWorkspacePageProps) {
  return (
    <div className="np-crud-workspace">
      <div className="np-crud-workspace-header">
        <div>
          <Title level={3} className="np-crud-workspace-title">{title}</Title>
          {description ? <Paragraph className="np-crud-workspace-description">{description}</Paragraph> : null}
        </div>
        {primaryActions ? <Space className="np-crud-workspace-actions">{primaryActions}</Space> : null}
      </div>
      {filterBar ? <div className="np-crud-workspace-filter">{filterBar}</div> : null}
      <div className="np-crud-workspace-content">{children}</div>
    </div>
  );
}
```

- [ ] **Step 2：新建 `CrudSearchBar.tsx`**

写入：

```tsx
import type { ReactNode } from 'react';
import { Button, Space } from 'antd';

export interface CrudSearchBarProps {
  fields?: ReactNode;
  loading?: boolean;
  onSearch: () => void;
  onReset: () => void;
  more?: ReactNode;
}

export function CrudSearchBar({ fields, loading = false, onSearch, onReset, more }: CrudSearchBarProps) {
  return (
    <div className="np-crud-search-bar">
      <div className="np-crud-search-fields">{fields}</div>
      <Space className="np-crud-search-actions">
        <Button type="primary" onClick={onSearch} loading={loading}>
          搜索
        </Button>
        <Button onClick={onReset} disabled={loading}>
          重置
        </Button>
        {more ?? <Button type="text">更多筛选</Button>}
      </Space>
    </div>
  );
}
```

- [ ] **Step 3：新建 `CrudTableSection.tsx`**

写入：

```tsx
import type { ReactNode } from 'react';
import { ReloadOutlined } from '@ant-design/icons';
import { Button } from 'antd';
import type { SearchTablePageProps } from './SearchTablePage';
import { SearchTablePage } from './SearchTablePage';

export interface CrudTableSectionProps<TRow extends Record<string, unknown>> extends SearchTablePageProps<TRow> {
  title: ReactNode;
  total: number;
  loading?: boolean;
  onRefresh: () => void;
}

export function CrudTableSection<TRow extends Record<string, unknown>>({
  title,
  total,
  loading = false,
  onRefresh,
  toolbar,
  ...tableProps
}: CrudTableSectionProps<TRow>) {
  return (
    <div className="np-crud-table-section">
      <SearchTablePage<TRow>
        {...tableProps}
        headerTitle={(
          <div className="np-list-card-title-wrap">
            <span className="np-list-card-title">{title}</span>
            <span className="np-list-card-count">{`共 ${total} 条`}</span>
          </div>
        )}
        toolbar={(
          <>
            <Button size="small" icon={<ReloadOutlined />} onClick={onRefresh} loading={loading}>
              刷新
            </Button>
            {toolbar}
          </>
        )}
      />
    </div>
  );
}
```

- [ ] **Step 4：新建 `CrudDetailDrawer.tsx`**

写入：

```tsx
import type { ReactNode } from 'react';
import { Drawer, Space } from 'antd';
import type { ProDescriptionsItemProps } from '@ant-design/pro-components';
import { AlertState } from './AlertState';
import { DetailPage } from './DetailPage';
import { LoadingBlock } from './LoadingBlock';

export interface CrudDetailDrawerProps<TRecord extends Record<string, unknown>> {
  open: boolean;
  title: ReactNode;
  dataSource: TRecord;
  columns: ProDescriptionsItemProps<TRecord>[];
  loading?: boolean;
  error?: string | null;
  actions?: ReactNode;
  summary?: ReactNode;
  onClose: () => void;
}

export function CrudDetailDrawer<TRecord extends Record<string, unknown>>({
  open,
  title,
  dataSource,
  columns,
  loading = false,
  error = null,
  actions,
  summary,
  onClose,
}: CrudDetailDrawerProps<TRecord>) {
  return (
    <Drawer
      open={open}
      title={title}
      width={720}
      onClose={onClose}
      extra={actions ? <Space>{actions}</Space> : null}
      destroyOnClose
    >
      {loading ? <LoadingBlock message="正在加载详情..." /> : null}
      {!loading && error ? <AlertState type="error" message={error} /> : null}
      {!loading && !error ? (
        <div className="np-crud-detail-drawer-body">
          {summary}
          <DetailPage title="详情信息" dataSource={dataSource} columns={columns} />
        </div>
      ) : null}
    </Drawer>
  );
}
```

- [ ] **Step 5：新建 `CrudFormDrawer.tsx`**

写入：

```tsx
import type { ReactNode } from 'react';
import { Button, Drawer, Space } from 'antd';
import { AlertState } from './AlertState';

export interface CrudFormDrawerProps {
  open: boolean;
  title: ReactNode;
  loading?: boolean;
  saving?: boolean;
  error?: string | null;
  children: ReactNode;
  onSubmit: () => void;
  onClose: () => void;
}

export function CrudFormDrawer({
  open,
  title,
  loading = false,
  saving = false,
  error = null,
  children,
  onSubmit,
  onClose,
}: CrudFormDrawerProps) {
  return (
    <Drawer
      open={open}
      title={title}
      width={720}
      onClose={onClose}
      destroyOnClose
      footer={(
        <Space className="np-crud-form-drawer-footer">
          <Button onClick={onClose} disabled={saving}>取消</Button>
          <Button type="primary" onClick={onSubmit} loading={saving} disabled={loading}>
            保存
          </Button>
        </Space>
      )}
    >
      {error ? <AlertState type="error" message={error} /> : null}
      {children}
    </Drawer>
  );
}
```

- [ ] **Step 6：新建 `CrudDangerAction.tsx`**

写入：

```tsx
import type { ReactNode } from 'react';
import { Button, Popconfirm } from 'antd';

export interface CrudDangerActionProps {
  children: ReactNode;
  confirmMessage: string;
  loading?: boolean;
  disabled?: boolean;
  onConfirm: () => void | Promise<void>;
}

export function CrudDangerAction({
  children,
  confirmMessage,
  loading = false,
  disabled = false,
  onConfirm,
}: CrudDangerActionProps) {
  return (
    <Popconfirm
      title={confirmMessage}
      okText="确认"
      cancelText="取消"
      okButtonProps={{ danger: true, loading }}
      onConfirm={() => void onConfirm()}
    >
      <Button type="link" danger loading={loading} disabled={disabled}>
        {children}
      </Button>
    </Popconfirm>
  );
}
```

- [ ] **Step 7：导出新 adapter**

在 `frontend/packages/ui/src/index.ts` 追加：

```ts
export * from './pro-adapters/CrudWorkspacePage';
export * from './pro-adapters/CrudSearchBar';
export * from './pro-adapters/CrudTableSection';
export * from './pro-adapters/CrudDetailDrawer';
export * from './pro-adapters/CrudFormDrawer';
export * from './pro-adapters/CrudDangerAction';
```

- [ ] **Step 8：构建 UI 包**

运行：`pnpm --dir frontend --filter @icp-platform/ui build`

预期：通过。如果 AntD `Drawer` 类型有版本差异，只调整受影响 prop，不扩大改动范围。

- [ ] **Step 9：提交**

```powershell
git add frontend/packages/ui/src/pro-adapters frontend/packages/ui/src/index.ts
git commit -m "功能：新增CRUD Pro页面组件"
```

## Task 2：扩展标准 CRUD 删除动作协议

**文件：**
- 修改：`frontend/packages/sdk/src/standardCrud.ts`
- 修改：`frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
- 测试：`frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`

- [ ] **Step 1：先写失败测试**

在 `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts` 增加：

```ts
import { createStandardCrudClient } from '@icp-platform/sdk';

it('通过标准客户端删除标准 CRUD 资源', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  const client = createStandardCrudClient('/api/sample-items', fetcher as unknown as typeof fetch);

  await client.delete('item-1');

  expect(fetcher).toHaveBeenCalledWith('/api/sample-items/item-1', { method: 'DELETE' });
});
```

- [ ] **Step 2：运行失败测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk`

预期：失败，原因是 `StandardCrudClient` 还没有 `delete`。

- [ ] **Step 3：扩展 SDK 类型和客户端**

在 `frontend/packages/sdk/src/standardCrud.ts` 中，将 `StandardCrudActionDefinition.standardAction` 改为：

```ts
standardAction?: 'create' | 'view' | 'edit' | 'refresh' | 'delete' | 'disable' | 'submit';
```

将 `StandardCrudClient` 改为：

```ts
export interface StandardCrudClient {
  list: (query?: Record<string, string | number | boolean | null | undefined>) => Promise<unknown>;
  getDetail: (id: string) => Promise<StandardCrudDetail>;
  create: (values: StandardCrudMutationValues) => Promise<StandardCrudDetail>;
  update: (id: string, values: StandardCrudMutationValues) => Promise<StandardCrudDetail>;
  delete: (id: string) => Promise<void>;
  disable: (id: string) => Promise<StandardCrudDetail>;
}
```

在 `createStandardCrudClient` 中增加：

```ts
delete: (id) =>
  requestJson<void>(
    fetcher,
    `${apiRoute}/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    '删除标准资源失败',
  ),
```

- [ ] **Step 4：更新动作运行时危险动作判断**

在 `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx` 中，确保 `delete` 即使没有显式 `variant: 'danger'` 也按危险动作渲染：

```tsx
const isDangerAction = action.variant === 'danger' || action.standardAction === 'delete';
```

使用 `isDangerAction` 驱动按钮危险样式和确认分支。不要从平台侧推断 `disable`，只有资源显式声明时才展示。

- [ ] **Step 5：再次运行测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk`

预期：通过。

- [ ] **Step 6：提交**

```powershell
git add frontend/packages/sdk/src/standardCrud.ts frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts
git commit -m "功能：扩展标准CRUD删除动作协议"
```

## Task 3：实现 Shell CRUD 工作台抽屉状态机

**文件：**
- 修改：`frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- 修改：`frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- 新建：`frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

- [ ] **Step 1：新增失败测试**

新建 `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`，先覆盖新建、查看、编辑都通过右侧抽屉打开：

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationModule from '../../../../modules/organization/src/index';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

async function flushAsync() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function renderHost(container: HTMLElement) {
  const root = createRoot(container);
  root.render(
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: {} }}>
      <RuntimeMetadataProvider
        value={{
          manifest: { modules: [] },
          navDocuments: [],
          viewDocuments: [{
            type: 'view',
            view: {
              key: 'organization.person.list',
              mode: 'list',
              titleKey: '人员中心',
              descriptionKey: '人员列表维护',
            },
          }],
          modelDocuments: [],
        }}
      >
        <StandardCrudListPageHost viewKey="organization.person.list" />
      </RuntimeMetadataProvider>
    </LocalizationProvider>,
  );
  return root;
}

describe('人员中心 CRUD Pro 样板', () => {
  let container: HTMLDivElement;
  let root: Root | null = null;

  afterEach(async () => {
    vi.restoreAllMocks();
    if (root) {
      await act(async () => root?.unmount());
    }
    container?.remove();
  });

  it('新建、查看、编辑都在右侧抽屉中打开', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      return new Response(JSON.stringify({
        items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const createButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.includes('新建人员'));
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).toContain('新建人员');
    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();

    const closeButton = document.body.querySelector('.ant-drawer-close') as HTMLButtonElement;
    await act(async () => closeButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));

    const viewButton = Array.from(container.querySelectorAll('button,a')).find((button) => button.textContent?.includes('查看'));
    await act(async () => viewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('详情信息');

    const editButton = Array.from(document.body.querySelectorAll('button,a')).find((button) => button.textContent?.includes('编辑'));
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).toContain('编辑人员');
  });
});
```

- [ ] **Step 2：运行失败测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

预期：失败，因为列表 Host 还没有抽屉状态机。

- [ ] **Step 3：在列表 Host 增加抽屉状态**

在 `StandardCrudListPageHost.tsx` 增加类型和初始状态：

```tsx
type CrudDrawerMode = 'closed' | 'create' | 'view' | 'edit';

interface CrudDrawerState {
  mode: CrudDrawerMode;
  id?: string;
  source: 'user-action' | 'route-compatibility';
}

const CLOSED_DRAWER_STATE: CrudDrawerState = {
  mode: 'closed',
  source: 'user-action',
};
```

在 `StandardCrudListPageContent` 中增加状态：

```tsx
const [drawerState, setDrawerState] = useState<CrudDrawerState>(CLOSED_DRAWER_STATE);
const [detailFields, setDetailFields] = useState<Record<string, unknown>>({});
const [detailError, setDetailError] = useState<string | null>(null);
const [detailLoading, setDetailLoading] = useState(false);
const [formValues, setFormValues] = useState<Record<string, unknown>>({});
const [saveError, setSaveError] = useState<string | null>(null);
const [saving, setSaving] = useState(false);
```

- [ ] **Step 4：新建动作改为打开抽屉**

在 `StandardCrudListPageHost.tsx` 增加：

```tsx
const openCreateDrawer = (source: CrudDrawerState['source'] = 'user-action') => {
  const initialValues = Object.fromEntries(
    (registration.fields ?? []).map((field) => [field.key, field.defaultValue ?? '']),
  );
  setFormValues(initialValues);
  setSaveError(null);
  setDrawerState({ mode: 'create', source });
};
```

页面标题区的“新建人员”按钮使用 `openCreateDrawer()`，不再默认执行人员模块里的 `window.location.assign`。

- [ ] **Step 5：增加查看/编辑抽屉加载函数**

增加：

```tsx
const openViewDrawer = async (id: string, source: CrudDrawerState['source'] = 'user-action') => {
  setDrawerState({ mode: 'view', id, source });
  setDetailLoading(true);
  setDetailError(null);
  try {
    const detail = await client.getDetail(id);
    setDetailFields(detail.fields);
    setFormValues(buildListHostFormValues(detail.fields, registration.fields ?? []));
  } catch (loadError) {
    setDetailError(loadError instanceof Error ? loadError.message : '加载详情失败');
  } finally {
    setDetailLoading(false);
  }
};

const openEditDrawer = async (id: string, source: CrudDrawerState['source'] = 'user-action') => {
  await openViewDrawer(id, source);
  setDrawerState({ mode: 'edit', id, source });
};
```

在文件底部增加：

```tsx
function buildListHostFormValues(
  fieldsRecord: Record<string, unknown>,
  fields: StandardCrudFieldDefinition[],
): Record<string, unknown> {
  return Object.fromEntries(fields.map((field) => [field.key, readStandardCrudFieldValue(fieldsRecord, field)]));
}
```

从 `@icp-platform/sdk` 引入 `readStandardCrudFieldValue` 和 `type StandardCrudFieldDefinition`。

- [ ] **Step 6：行操作改由平台渲染查看/编辑**

在操作列中渲染：

```tsx
<AntdButton type="link" onClick={() => void openViewDrawer(registration.getRowId(row))}>查看</AntdButton>
<AntdButton type="link" onClick={() => void openEditDrawer(registration.getRowId(row))}>编辑</AntdButton>
```

过滤 `rowActionDefinitions` 时排除 `create`、`refresh`、`view`、`edit`、`delete`，只保留资源显式声明的可选动作，例如 `disable`。

- [ ] **Step 7：渲染详情抽屉和表单抽屉**

从 `@icp-platform/ui` 引入：

```tsx
import {
  CrudDetailDrawer,
  CrudFormDrawer,
  CrudSearchBar,
  CrudTableSection,
  CrudWorkspacePage,
  ErrorState,
} from '@icp-platform/ui';
```

在 Host 中接入 lookup runtime：

```tsx
const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
  fields: registration.fields ?? [],
  providers: registration.lookupProviders ?? [],
  values: formValues,
  detailFields,
});
```

从 `@icp-platform/sdk` 引入 `useStandardCrudLookupRuntime` 和 `applyStandardCrudLookupValueChange`，表单 `onChange` 使用：

```tsx
onChange={(fieldKey, value) => {
  setFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, registration.fields ?? []));
}}
```

在工作台内容后渲染：

```tsx
<CrudDetailDrawer
  open={drawerState.mode === 'view'}
  title={t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey)}
  dataSource={buildDetailPreviewData(detailFields, registration.fields ?? [])}
  columns={buildDetailColumns(detailFields, registration.fields ?? [])}
  loading={detailLoading}
  error={detailError}
  actions={<AntdButton type="primary" onClick={() => drawerState.id && void openEditDrawer(drawerState.id)}>编辑</AntdButton>}
  onClose={() => setDrawerState(CLOSED_DRAWER_STATE)}
/>
<CrudFormDrawer
  open={drawerState.mode === 'create' || drawerState.mode === 'edit'}
  title={drawerState.mode === 'create' ? '新建人员' : '编辑人员'}
  saving={saving}
  error={saveError}
  onSubmit={() => void handleDrawerSave()}
  onClose={() => setDrawerState(CLOSED_DRAWER_STATE)}
>
  <StandardCrudForm
    formId={`${schema.resourceKey}-drawer-form`}
    mode={drawerState.mode === 'create' ? 'create' : 'edit'}
    fields={registration.fields ?? []}
    sections={registration.formSections ?? []}
    values={formValues}
    lookupOptions={lookupOptions}
    lookupLoading={lookupLoading}
    lookupErrors={lookupErrors}
    onChange={(fieldKey, value) => {
      setFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, registration.fields ?? []));
    }}
    onSubmit={() => void handleDrawerSave()}
    saving={saving}
    error={saveError}
    titleForSection={(titleKey) => titleKey}
  />
</CrudFormDrawer>
```

- [ ] **Step 8：实现抽屉保存**

增加：

```tsx
const handleDrawerSave = async () => {
  try {
    setSaving(true);
    setSaveError(null);
    const fields = registration.fields ?? [];
    const values = registration.buildMutationValues
      ? registration.buildMutationValues(formValues)
      : normalizeStandardCrudSubmitValues(fields, formValues);

    if (drawerState.mode === 'create') {
      await client.create(values);
    } else if (drawerState.mode === 'edit' && drawerState.id) {
      await client.update(drawerState.id, values);
    }

    context.refresh();
    setDrawerState(CLOSED_DRAWER_STATE);
  } catch (saveFailure) {
    setSaveError(saveFailure instanceof Error ? saveFailure.message : '保存标准资源失败');
  } finally {
    setSaving(false);
  }
};
```

从 SDK 引入 `normalizeStandardCrudSubmitValues`。

- [ ] **Step 9：运行抽屉测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

预期：通过。

- [ ] **Step 10：提交**

```powershell
git add frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx
git commit -m "功能：人员中心接入CRUD抽屉样板"
```

## Task 4：人员资源接入 AntD 筛选和可选动作

**文件：**
- 修改：`frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- 修改：`frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`

- [ ] **Step 1：更新列表布局测试期望**

在 `organization-person-center-list-layout.test.tsx` 中，将原生控件断言改为 AntD 控件断言：

```ts
expect(container.querySelector('.ant-input[placeholder="按工号 / 姓名搜索"]')).not.toBeNull();
expect(container.querySelector('.ant-select')).not.toBeNull();
expect(container.textContent).toContain('更多筛选');
expect(container.textContent).toContain('停用');
```

增加行操作不再直接跳转的断言：

```ts
expect(container.querySelector('a[href="/organization/persons/person-1"]')).toBeNull();
```

- [ ] **Step 2：运行失败测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- organization-person-center-list-layout`

预期：失败，因为筛选仍是原生控件，行操作仍由模块私有导航控制。

- [ ] **Step 3：将筛选控件替换为 AntD**

在 `personStandardCrudResource.tsx` 中调整 import：

```tsx
import { Input, Select } from 'antd';
import { Button, ErrorState, StatusTag } from '@icp-platform/ui';
```

将关键字 `<input>` 替换为：

```tsx
<Input
  aria-label="按工号 / 姓名搜索"
  className="np-filter-control np-filter-control-keyword"
  value={filters.keyword}
  onChange={(event) => setFilters((current) => ({ ...current, keyword: event.target.value }))}
  placeholder="按工号 / 姓名搜索"
/>
```

将公司 `<select>` 替换为：

```tsx
<Select
  aria-label="公司"
  className="np-filter-control"
  value={filters.companyId || undefined}
  onChange={(value) => setFilters((current) => ({ ...current, companyId: value ?? '', departmentId: '' }))}
  placeholder="全部公司"
  allowClear
  options={companyOptions}
/>
```

将部门 `<select>` 替换为：

```tsx
<Select
  aria-label="部门"
  className="np-filter-control"
  value={filters.departmentId || undefined}
  onChange={(value) => setFilters((current) => ({ ...current, departmentId: value ?? '' }))}
  placeholder="请选择部门"
  allowClear
  disabled={!filters.companyId}
  options={departmentOptions}
/>
```

将在职状态 `<select>` 替换为：

```tsx
<Select
  aria-label="在职状态"
  className="np-filter-control"
  value={filters.employmentStatus || undefined}
  onChange={(value) => setFilters((current) => ({ ...current, employmentStatus: value ?? '' }))}
  placeholder="全部状态"
  allowClear
  options={employmentStatusOptions}
/>
```

- [ ] **Step 4：行操作不再拥有页面跳转**

移除 `PersonRowActions` 里的查看/编辑 `window.location.assign`。让 Shell 统一渲染 `view/edit`。

资源动作声明改为：

```ts
listActions: [
  { key: 'create', labelKey: '新建人员', variant: 'primary', standardAction: 'create' },
  { key: 'refresh', labelKey: '刷新', variant: 'default', standardAction: 'refresh' },
  { key: 'view', labelKey: '查看', standardAction: 'view' },
  { key: 'edit', labelKey: '编辑', standardAction: 'edit' },
  { key: 'disable', labelKey: '停用', variant: 'danger', standardAction: 'disable', confirmMessage: '确认停用该人员？' },
],
```

不要给人员资源增加 `delete`，除非后端删除接口和独立权限已经稳定存在。

- [ ] **Step 5：收口 action handler 边界**

移除 `create` 的直接浏览器跳转。保留：

```ts
actionHandlers: {
  refresh: ({ refresh }) => {
    refresh();
  },
  disable: async ({ id, refresh }) => {
    if (!id) {
      throw new Error('缺少人员标识，无法停用。');
    }

    await disablePerson(id);
    refresh();
  },
},
```

- [ ] **Step 6：运行人员列表测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- organization-person-center-list-layout`

预期：通过。

- [ ] **Step 7：提交**

```powershell
git add frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx
git commit -m "重构：人员中心筛选与动作接入平台样板"
```

## Task 5：收口删除协议和刷新行为

**文件：**
- 修改：`frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- 修改：`frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

- [ ] **Step 1：增加删除协议失败测试**

在 `person-center-crud-pro-sample.test.tsx` 追加：

```tsx
it('人员中心未声明删除时不展示删除入口', async () => {
  vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
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

  expect(container.textContent).not.toContain('删除');
  expect(container.textContent).toContain('停用');
});
```

再增加一个临时资源显式声明 `delete` 的测试：

```tsx
it('资源声明删除时按危险确认动作渲染', async () => {
  const deleteHandler = vi.fn();
  const sampleModule = {
    ...organizationModule,
    standardCrudResources: [{
      ...organizationModule.standardCrudResources![0],
      listActions: [
        { key: 'delete', labelKey: '删除', standardAction: 'delete', confirmMessage: '确认删除该人员吗？' },
      ],
      actionHandlers: { delete: deleteHandler },
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

  const deleteButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.includes('删除'));
  expect(deleteButton).not.toBeUndefined();

  await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
  expect(document.body.textContent).toContain('确认删除该人员吗？');
});
```

- [ ] **Step 2：运行失败测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

预期：失败，直到 `delete` 只在资源声明时出现，并通过 `CrudDangerAction` 渲染。

- [ ] **Step 3：实现声明式删除渲染**

在 `StandardCrudListPageHost.tsx` 的行级动作映射中，对 `delete` 使用：

```tsx
{action.standardAction === 'delete' ? (
  <CrudDangerAction
    key={action.key}
    confirmMessage={action.confirmMessage ?? `确认${action.labelKey}吗？`}
    onConfirm={async () => {
      const id = registration.getRowId(row);
      if (registration.actionHandlers?.[action.key]) {
        await registration.actionHandlers[action.key]({ id, row, refresh: context.refresh });
      } else {
        await client.delete(id);
        context.refresh();
      }
      if (drawerState.id === id) {
        setDrawerState(CLOSED_DRAWER_STATE);
      }
    }}
  >
    {action.labelKey}
  </CrudDangerAction>
) : null}
```

不要在资源未声明时合成删除按钮。

- [ ] **Step 4：增加刷新不重置筛选的测试**

在 `person-center-crud-pro-sample.test.tsx` 增加：

```tsx
it('刷新当前查询时不重置筛选条件', async () => {
  vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
  const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
    items: [],
    total: 0,
    page: 1,
    pageSize: 20,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = renderHost(container);
  });
  await flushAsync();

  const keyword = container.querySelector('input[placeholder="按工号 / 姓名搜索"]') as HTMLInputElement;
  await act(async () => {
    keyword.value = 'P0001';
    keyword.dispatchEvent(new Event('input', { bubbles: true }));
  });

  const search = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.includes('搜索'));
  await act(async () => search?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
  await flushAsync();

  const refresh = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.includes('刷新'));
  await act(async () => refresh?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
  await flushAsync();

  expect(fetcher.mock.calls.at(-1)?.[0]).toContain('keyword=P0001');
});
```

- [ ] **Step 5：运行删除与刷新测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

预期：通过。

- [ ] **Step 6：提交**

```powershell
git add frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx
git commit -m "功能：收口CRUD删除与刷新协议"
```

## Task 6：兼容旧路由入口

**文件：**
- 修改：`frontend/packages/sdk/src/standardCrud.ts`
- 修改：`frontend/packages/shell/src/App.tsx`
- 修改：`frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`
- 修改：`frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- 修改：`frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx`
- 修改：`frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

- [ ] **Step 1：增加查询参数打开抽屉的失败测试**

在 `person-center-crud-pro-sample.test.tsx` 追加：

```tsx
it('通过路由兼容查询参数打开新建抽屉', async () => {
  window.history.pushState({}, '', '/organization/persons?drawer=create');
  vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
    items: [],
    total: 0,
    page: 1,
    pageSize: 20,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = renderHost(container);
  });
  await flushAsync();

  expect(document.body.textContent).toContain('新建人员');
  expect(document.body.querySelector('.ant-drawer')).not.toBeNull();
});
```

- [ ] **Step 2：运行失败测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

预期：失败，因为列表 Host 尚未读取 `drawer` 查询参数。

- [ ] **Step 3：列表 Host 读取抽屉查询参数**

在 `StandardCrudListPageHost.tsx` 中引入 `useLocation`，并增加：

```tsx
const location = useLocation();

useEffect(() => {
  const params = new URLSearchParams(location.search);
  const drawer = params.get('drawer');
  const id = params.get('id') ?? undefined;

  if (drawer === 'create') {
    openCreateDrawer('route-compatibility');
  }
  if (drawer === 'view' && id) {
    void openViewDrawer(id, 'route-compatibility');
  }
  if (drawer === 'edit' && id) {
    void openEditDrawer(id, 'route-compatibility');
  }
}, [location.search]);
```

将 `openCreateDrawer/openViewDrawer/openEditDrawer` 的 `source` 参数默认值设为 `'user-action'`。

- [ ] **Step 4：旧新建路由重定向到抽屉状态**

在 `App.tsx` 中增加：

```tsx
function RedirectToPersonCreateDrawer() {
  return <Navigate to="/organization/persons?drawer=create" replace />;
}
```

在动态详情路由前注册：

```tsx
<Route path="/organization/persons/new" element={<RedirectToPersonCreateDrawer />} />
```

- [ ] **Step 5：增加 route adapter 测试和实现**

在 `standard-crud-route-adapter.test.tsx` 中引入新 helper：

```ts
import {
  buildStandardCrudDetailRouteEntries,
  buildStandardCrudDrawerRouteRedirects,
  filterFallbackRoutesByStandardCrudRoutes,
} from '../navigation/standardCrudRouteAdapter';
```

增加测试：

```ts
it('为抽屉入口兼容构建旧新建路由重定向', () => {
  expect(buildStandardCrudDrawerRouteRedirects(modules)).toEqual([
    {
      from: '/organization/persons/new',
      to: '/organization/persons?drawer=create',
    },
  ]);
});
```

给测试 fixture 中的资源增加：

```ts
listRoutePath: '/organization/persons',
```

在 `frontend/packages/sdk/src/standardCrud.ts` 的 `StandardCrudResourceRegistration` 中增加：

```ts
listRoutePath?: string;
```

在 `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx` 的人员资源注册中增加：

```ts
listRoutePath: '/organization/persons',
```

在 `standardCrudRouteAdapter.ts` 中增加：

```ts
export interface StandardCrudDrawerRouteRedirect {
  from: string;
  to: string;
}

export function buildStandardCrudDrawerRouteRedirects(modules: FrontendModule[]): StandardCrudDrawerRouteRedirect[] {
  return modules.flatMap((module) =>
    (module.standardCrudResources ?? []).flatMap((resource) => {
      if (!resource.listRoutePath) {
        return [];
      }

      return [{
        from: `${resource.listRoutePath}/new`,
        to: `${resource.listRoutePath}?drawer=create`,
      }];
    }),
  );
}
```

- [ ] **Step 6：运行路由兼容测试**

运行：`pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample standard-crud-route-adapter`

预期：通过。

- [ ] **Step 7：提交**

```powershell
git add frontend/packages/sdk/src/standardCrud.ts frontend/packages/shell/src/App.tsx frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx
git commit -m "功能：兼容人员中心抽屉路由入口"
```

## Task 7：最终验证与文档收口

**文件：**
- 修改：`docs/verification/frontend-ant-design-rebuild-verification.md`
- 修改：`docs/phases/current-handoff.md`

- [ ] **Step 1：运行定向测试**

运行：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout organization-person-center-detail-layout standard-crud-sdk
```

预期：通过。

- [ ] **Step 2：运行构建**

运行：

```powershell
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-platform/shell build
pnpm --dir frontend --filter @icp-modules/organization build
```

预期：通过。Vite chunk size warning 可以接受，只要命令退出码为 0。

- [ ] **Step 3：更新验证记录**

在 `docs/verification/frontend-ant-design-rebuild-verification.md` 追加：

```md
## 人员中心后台 CRUD Pro 样板固化验证

- 范围：标准 CRUD 列表工作台、详情抽屉、表单抽屉、刷新、删除协议、可选停用动作、旧新建路由兼容。
- 结果：人员中心新增/查看/编辑默认使用右侧抽屉；独立详情页保留为深链接和回滚出口；`disable` 作为人员资源可选动作保留；`delete` 仅在资源显式声明时展示并走确认协议。
- 验证命令：
  - `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout organization-person-center-detail-layout standard-crud-sdk`
  - `pnpm --dir frontend --filter @icp-platform/ui build`
  - `pnpm --dir frontend --filter @icp-platform/shell build`
  - `pnpm --dir frontend --filter @icp-modules/organization build`
- 已知风险：人员中心若后端暂无删除接口，则删除仅作为平台协议能力保留，不展示人员删除入口。
- 回滚：回退 UI CRUD Pro adapter、Shell standard CRUD Host 抽屉状态机、路由兼容和人员资源动作声明；独立详情页仍可作为回滚入口。
```

- [ ] **Step 4：更新交接台账**

在 `docs/phases/current-handoff.md` 最近完成中新增：

```md
### 4.x 人员中心后台 CRUD Pro 样板固化

状态：已完成并验证。

完成内容：

- 人员中心作为平台第一版完整后台 CRUD 样板，覆盖列表、详情抽屉和表单抽屉。
- 新增、查看、编辑默认在右侧抽屉完成，独立详情页保留为深链接和回滚出口。
- 标准动作集收敛为 `create / refresh / view / edit / delete`，`disable` 明确为可选标准动作。
- 人员中心未强制展示删除入口；只有资源显式声明并具备后端能力时才展示删除。
```

同步调整“当前推荐下一步”，不要再默认把配置管理作为下一步。

- [ ] **Step 5：检查工作区**

运行：

```powershell
git status --short
```

预期：只看到验证记录和交接台账的预期修改。

- [ ] **Step 6：提交**

```powershell
git add docs/verification/frontend-ant-design-rebuild-verification.md docs/phases/current-handoff.md
git commit -m "验证：记录人员中心CRUD样板固化结果"
```

## 计划自检

- 规格覆盖：任务覆盖 UI adapter、Shell Host 编排、Organization 资源收缩、`create / refresh / view / edit / delete`、可选 `disable`、右侧抽屉、独立详情页回退、旧路由兼容、验证记录和交接台账。
- 占位扫描：无 `TBD`、`TODO` 或 “implement later” 这类占位；人员删除入口的条件是明确规则：只有资源声明且后端能力存在时才展示。
- 类型一致性：`CrudDrawerState`、必备/可选动作、`StandardCrudClient.delete`、`listRoutePath` 和 UI adapter 命名在各任务中保持一致。
