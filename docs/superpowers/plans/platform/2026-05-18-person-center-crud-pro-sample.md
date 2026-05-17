# Person Center CRUD Pro Sample Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a reusable Ant Design Pro-style standard CRUD workspace and make Person Center the first complete list/detail/form drawer sample.

**Architecture:** Keep platform contracts as the source of truth. `frontend/packages/ui` owns business-free CRUD visual adapters, `frontend/packages/shell` orchestrates standard CRUD state, routing, permissions, actions, requests, drawers, and errors, and `frontend/modules/organization` only declares the person resource fields, filters, columns, lookup providers, summaries, and handlers.

**Tech Stack:** React 18, TypeScript, Vite, Vitest, Ant Design v5, Ant Design Pro Components, pnpm workspace.

---

## File Structure

- Create `frontend/packages/ui/src/pro-adapters/CrudWorkspacePage.tsx`
  - Business-free page frame for title, description, primary actions, filter region, and content.
- Create `frontend/packages/ui/src/pro-adapters/CrudSearchBar.tsx`
  - Business-free AntD search bar layout with search/reset/more slots.
- Create `frontend/packages/ui/src/pro-adapters/CrudTableSection.tsx`
  - Business-free wrapper around `SearchTablePage` with list title, total, refresh toolbar, and table content.
- Create `frontend/packages/ui/src/pro-adapters/CrudDetailDrawer.tsx`
  - Business-free right-side detail drawer using `DetailPage`, summary slot, error/loading states, and action area.
- Create `frontend/packages/ui/src/pro-adapters/CrudFormDrawer.tsx`
  - Business-free right-side form drawer using `FormPage` and a sticky footer slot.
- Create `frontend/packages/ui/src/pro-adapters/CrudDangerAction.tsx`
  - Business-free danger action wrapper using AntD `Popconfirm`, loading, and error rendering.
- Modify `frontend/packages/ui/src/index.ts`
  - Export the new CRUD Pro adapters.
- Modify `frontend/packages/sdk/src/standardCrud.ts`
  - Add `delete` to standard action definitions and `delete` to `StandardCrudClient`.
- Modify `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - Convert the current list host into the CRUD workspace orchestrator with drawer state, detail loading, form loading, refresh, create, view, edit, delete, and optional actions.
- Modify `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
  - Keep independent detail page as a compatibility/deep-link entry while reusing the same detail/form rendering helpers introduced for drawers.
- Modify `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
  - Treat `delete` as a standard danger action and keep `disable` optional.
- Modify `frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`
  - Add route compatibility metadata for old create/edit URLs.
- Modify `frontend/packages/shell/src/App.tsx`
  - Redirect `/organization/persons/new` to the list route with drawer query state.
- Modify `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
  - Replace native filter controls with AntD controls, remove direct row navigation, and declare standard actions instead of private row layout.
- Create `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
  - Cover drawer-based create/view/edit, refresh, delete protocol, optional disable, and route compatibility.
- Modify existing tests:
  - `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
  - `frontend/packages/shell/src/__tests__/organization-person-center-detail-layout.test.tsx`
  - `frontend/packages/shell/src/__tests__/standard-crud-detail-host.test.tsx`

## Task 1: UI CRUD Pro Adapter Components

**Files:**
- Create: `frontend/packages/ui/src/pro-adapters/CrudWorkspacePage.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/CrudSearchBar.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/CrudTableSection.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/CrudDetailDrawer.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/CrudFormDrawer.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/CrudDangerAction.tsx`
- Modify: `frontend/packages/ui/src/index.ts`
- Test: `pnpm --dir frontend --filter @icp-platform/ui build`

- [ ] **Step 1: Create `CrudWorkspacePage.tsx`**

Add this file:

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

- [ ] **Step 2: Create `CrudSearchBar.tsx`**

Add this file:

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

- [ ] **Step 3: Create `CrudTableSection.tsx`**

Add this file:

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

- [ ] **Step 4: Create `CrudDetailDrawer.tsx`**

Add this file:

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

- [ ] **Step 5: Create `CrudFormDrawer.tsx`**

Add this file:

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

- [ ] **Step 6: Create `CrudDangerAction.tsx`**

Add this file:

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

- [ ] **Step 7: Export the new adapters**

Append to `frontend/packages/ui/src/index.ts`:

```ts
export * from './pro-adapters/CrudWorkspacePage';
export * from './pro-adapters/CrudSearchBar';
export * from './pro-adapters/CrudTableSection';
export * from './pro-adapters/CrudDetailDrawer';
export * from './pro-adapters/CrudFormDrawer';
export * from './pro-adapters/CrudDangerAction';
```

- [ ] **Step 8: Build UI package**

Run: `pnpm --dir frontend --filter @icp-platform/ui build`

Expected: PASS. If TypeScript fails because AntD `Drawer` prop names differ, adjust only the affected prop names in the adapter files.

- [ ] **Step 9: Commit**

```powershell
git add frontend/packages/ui/src/pro-adapters frontend/packages/ui/src/index.ts
git commit -m "功能：新增CRUD Pro页面组件"
```

## Task 2: Standard CRUD Delete Action Contract

**Files:**
- Modify: `frontend/packages/sdk/src/standardCrud.ts`
- Modify: `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
- Test: `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`

- [ ] **Step 1: Write the failing SDK test**

Modify `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts` by adding:

```ts
import { createStandardCrudClient } from '@icp-platform/sdk';

it('deletes a standard CRUD resource through the standard client', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  const client = createStandardCrudClient('/api/sample-items', fetcher as unknown as typeof fetch);

  await client.delete('item-1');

  expect(fetcher).toHaveBeenCalledWith('/api/sample-items/item-1', { method: 'DELETE' });
});
```

- [ ] **Step 2: Run the failing SDK test**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk`

Expected: FAIL with TypeScript or runtime error because `StandardCrudClient` does not expose `delete`.

- [ ] **Step 3: Extend the SDK standard action and client types**

In `frontend/packages/sdk/src/standardCrud.ts`, update `StandardCrudActionDefinition.standardAction`:

```ts
standardAction?: 'create' | 'view' | 'edit' | 'refresh' | 'delete' | 'disable' | 'submit';
```

Update `StandardCrudClient`:

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

Add this method inside `createStandardCrudClient`:

```ts
delete: (id) =>
  requestJson<void>(
    fetcher,
    `${apiRoute}/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    '删除标准资源失败',
  ),
```

- [ ] **Step 4: Update standard action runtime danger handling**

In `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`, ensure actions with `standardAction === 'delete'` are rendered as danger actions even when `variant` is omitted:

```tsx
const isDangerAction = action.variant === 'danger' || action.standardAction === 'delete';
```

Use `isDangerAction` for the AntD/Button danger style and confirmation branch. Keep `disable` optional: do not infer `disable` unless the resource explicitly declares it.

- [ ] **Step 5: Run SDK test again**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add frontend/packages/sdk/src/standardCrud.ts frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts
git commit -m "功能：扩展标准CRUD删除动作协议"
```

## Task 3: Shell CRUD Workspace Host State Machine

**Files:**
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- Test: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

- [ ] **Step 1: Create failing drawer behavior test**

Create `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx` with this initial test:

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

describe('person center CRUD Pro sample', () => {
  let container: HTMLDivElement;
  let root: Root | null = null;

  afterEach(async () => {
    vi.restoreAllMocks();
    if (root) {
      await act(async () => root?.unmount());
    }
    container?.remove();
  });

  it('opens create, view, and edit flows in right-side drawers', async () => {
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

- [ ] **Step 2: Run the failing drawer test**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

Expected: FAIL because `StandardCrudListPageHost` still navigates/uses module row actions instead of drawer state.

- [ ] **Step 3: Add drawer state types and helpers**

In `StandardCrudListPageHost.tsx`, add:

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

Add state in `StandardCrudListPageContent`:

```tsx
const [drawerState, setDrawerState] = useState<CrudDrawerState>(CLOSED_DRAWER_STATE);
const [detailFields, setDetailFields] = useState<Record<string, unknown>>({});
const [detailError, setDetailError] = useState<string | null>(null);
const [detailLoading, setDetailLoading] = useState(false);
const [formValues, setFormValues] = useState<Record<string, unknown>>({});
const [saveError, setSaveError] = useState<string | null>(null);
const [saving, setSaving] = useState(false);
```

- [ ] **Step 4: Replace header create action with drawer open**

In `StandardCrudListPageHost.tsx`, replace the create button handler with:

```tsx
const openCreateDrawer = () => {
  const initialValues = Object.fromEntries(
    (registration.fields ?? []).map((field) => [field.key, field.defaultValue ?? '']),
  );
  setFormValues(initialValues);
  setSaveError(null);
  setDrawerState({ mode: 'create', source: 'user-action' });
};
```

Use `openCreateDrawer` for the `create` button instead of `registration.actionHandlers.create`.

- [ ] **Step 5: Add view/edit drawer loaders**

Add:

```tsx
const openViewDrawer = async (id: string) => {
  setDrawerState({ mode: 'view', id, source: 'user-action' });
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

const openEditDrawer = async (id: string) => {
  await openViewDrawer(id);
  setDrawerState({ mode: 'edit', id, source: 'user-action' });
};
```

Add helper at file bottom:

```tsx
function buildListHostFormValues(
  fieldsRecord: Record<string, unknown>,
  fields: StandardCrudFieldDefinition[],
): Record<string, unknown> {
  return Object.fromEntries(fields.map((field) => [field.key, readStandardCrudFieldValue(fieldsRecord, field)]));
}
```

Import `readStandardCrudFieldValue` and `type StandardCrudFieldDefinition` from `@icp-platform/sdk`.

- [ ] **Step 6: Render row actions through platform actions**

In the action column render, prefer platform actions:

```tsx
<AntdButton type="link" onClick={() => void openViewDrawer(registration.getRowId(row))}>查看</AntdButton>
<AntdButton type="link" onClick={() => void openEditDrawer(registration.getRowId(row))}>编辑</AntdButton>
```

Keep optional module actions by filtering out `create`, `refresh`, `view`, `edit`, and `delete` from `rowActionDefinitions`. This preserves `disable` only when the resource declares it.

- [ ] **Step 7: Render `CrudDetailDrawer` and `CrudFormDrawer`**

Import:

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

Render below the workspace content:

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
    onChange={(fieldKey, value) => setFormValues((current) => ({ ...current, [fieldKey]: value }))}
    onSubmit={() => void handleDrawerSave()}
    saving={saving}
    error={saveError}
    titleForSection={(titleKey) => titleKey}
  />
</CrudFormDrawer>
```

Before rendering the drawers, wire lookup runtime in `StandardCrudListPageHost.tsx`:

```tsx
const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
  fields: registration.fields ?? [],
  providers: registration.lookupProviders ?? [],
  values: formValues,
  detailFields,
});
```

Import `useStandardCrudLookupRuntime` and `applyStandardCrudLookupValueChange` from `@icp-platform/sdk`, then replace the form `onChange` handler:

```tsx
onChange={(fieldKey, value) => {
  setFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, registration.fields ?? []));
}}
```

- [ ] **Step 8: Implement `handleDrawerSave`**

Add:

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

Import `normalizeStandardCrudSubmitValues`.

- [ ] **Step 9: Run drawer test**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

Expected: PASS.

- [ ] **Step 10: Commit**

```powershell
git add frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx
git commit -m "功能：人员中心接入CRUD抽屉样板"
```

## Task 4: Person Resource AntD Filters and Optional Actions

**Files:**
- Modify: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Modify: `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`

- [ ] **Step 1: Update list layout test expectations**

In `organization-person-center-list-layout.test.tsx`, replace assertions that look for native inputs with AntD control assertions:

```ts
expect(container.querySelector('.ant-input[placeholder="按工号 / 姓名搜索"]')).not.toBeNull();
expect(container.querySelector('.ant-select')).not.toBeNull();
expect(container.textContent).toContain('更多筛选');
expect(container.textContent).toContain('停用');
```

Add assertion that direct navigation is gone from row actions:

```ts
expect(container.querySelector('a[href="/organization/persons/person-1"]')).toBeNull();
```

- [ ] **Step 2: Run failing person layout test**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- organization-person-center-list-layout`

Expected: FAIL because filters still use native controls and row actions still own direct navigation.

- [ ] **Step 3: Replace native filter controls with AntD**

In `personStandardCrudResource.tsx`, change imports:

```tsx
import { Input, Select } from 'antd';
import { Button, ErrorState, StatusTag } from '@icp-platform/ui';
```

Replace `<input>` with:

```tsx
<Input
  aria-label="按工号 / 姓名搜索"
  className="np-filter-control np-filter-control-keyword"
  value={filters.keyword}
  onChange={(event) => setFilters((current) => ({ ...current, keyword: event.target.value }))}
  placeholder="按工号 / 姓名搜索"
/>
```

Replace company `<select>` with:

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

Replace department `<select>` with:

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

Replace employment status `<select>` with:

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

- [ ] **Step 4: Stop row actions from owning navigation**

Remove the `PersonRowActions` direct `window.location.assign` view/edit buttons. Keep only optional `disable` if the resource declares it, or remove `renderRowActions` entirely and let Shell render `view/edit` as platform standard actions.

Use this resource action declaration:

```ts
listActions: [
  { key: 'create', labelKey: '新建人员', variant: 'primary', standardAction: 'create' },
  { key: 'refresh', labelKey: '刷新', variant: 'default', standardAction: 'refresh' },
  { key: 'view', labelKey: '查看', standardAction: 'view' },
  { key: 'edit', labelKey: '编辑', standardAction: 'edit' },
  { key: 'disable', labelKey: '停用', variant: 'danger', standardAction: 'disable', confirmMessage: '确认停用该人员？' },
],
```

Do not add `delete` to person resource unless a stable backend delete endpoint and permission exist.

- [ ] **Step 5: Update action handler boundaries**

Keep `create` and `refresh` handlers for compatibility only if Shell still calls them as fallbacks. Remove direct browser navigation from `create`:

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

- [ ] **Step 6: Run person layout test**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- organization-person-center-list-layout`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx
git commit -m "重构：人员中心筛选与动作接入平台样板"
```

## Task 5: Delete Protocol and Refresh Behavior

**Files:**
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

- [ ] **Step 1: Add failing delete protocol test**

Append to `person-center-crud-pro-sample.test.tsx`:

```tsx
it('keeps delete hidden for person center until the resource declares it', async () => {
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

Add another test using a local sample module that declares `delete`, and assert `Popconfirm` appears:

```tsx
it('renders declared delete as a confirmed danger action', async () => {
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

- [ ] **Step 2: Run failing delete protocol tests**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

Expected: FAIL until `delete` is rendered only when declared and as `CrudDangerAction`.

- [ ] **Step 3: Implement declared delete rendering**

In `StandardCrudListPageHost.tsx`, when mapping row action definitions, render `delete` via `CrudDangerAction`:

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

Do not synthesize a delete action when the resource did not declare it.

- [ ] **Step 4: Ensure refresh does not reset filters**

Add a test in `person-center-crud-pro-sample.test.tsx`:

```tsx
it('refreshes the current query without resetting filters', async () => {
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

- [ ] **Step 5: Run delete and refresh tests**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx
git commit -m "功能：收口CRUD删除与刷新协议"
```

## Task 6: Legacy Route Compatibility

**Files:**
- Modify: `frontend/packages/shell/src/App.tsx`
- Modify: `frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`
- Modify: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

- [ ] **Step 1: Add failing route compatibility test**

Append to `person-center-crud-pro-sample.test.tsx`:

```tsx
it('opens create drawer from route compatibility query state', async () => {
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

- [ ] **Step 2: Run failing route test**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample`

Expected: FAIL because `StandardCrudListPageHost` does not read query drawer state yet.

- [ ] **Step 3: Read drawer query state in list host**

In `StandardCrudListPageHost.tsx`, import `useLocation` and add:

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

Change open helpers to accept `source: CrudDrawerState['source'] = 'user-action'`.

- [ ] **Step 4: Redirect legacy new route**

In `App.tsx`, add a route for `/organization/persons/new` that redirects to the list query state:

```tsx
function RedirectToPersonCreateDrawer() {
  return <Navigate to="/organization/persons?drawer=create" replace />;
}
```

Register it before the dynamic detail route:

```tsx
<Route path="/organization/persons/new" element={<RedirectToPersonCreateDrawer />} />
```

- [ ] **Step 5: Add route adapter test**

In `standard-crud-route-adapter.test.tsx`, import the new helper:

```ts
import {
  buildStandardCrudDetailRouteEntries,
  buildStandardCrudDrawerRouteRedirects,
  filterFallbackRoutesByStandardCrudRoutes,
} from '../navigation/standardCrudRouteAdapter';
```

Add this test:

```ts
it('builds legacy create route redirects for drawer entry compatibility', () => {
  expect(buildStandardCrudDrawerRouteRedirects(modules)).toEqual([
    {
      from: '/organization/persons/new',
      to: '/organization/persons?drawer=create',
    },
  ]);
});
```

Update the local `modules` fixture resource with a list route path:

```ts
listRoutePath: '/organization/persons',
```

In `frontend/packages/sdk/src/standardCrud.ts`, add the optional resource property used by the route adapter:

```ts
listRoutePath?: string;
```

In `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`, add the concrete list route to the person resource registration:

```ts
listRoutePath: '/organization/persons',
```

In `standardCrudRouteAdapter.ts`, add:

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

- [ ] **Step 6: Run route tests**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample standard-crud-route-adapter`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add frontend/packages/sdk/src/standardCrud.ts frontend/packages/shell/src/App.tsx frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx
git commit -m "功能：兼容人员中心抽屉路由入口"
```

## Task 7: Final Verification and Documentation Update

**Files:**
- Modify: `docs/verification/frontend-ant-design-rebuild-verification.md`
- Modify: `docs/phases/current-handoff.md`

- [ ] **Step 1: Run focused tests**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout organization-person-center-detail-layout standard-crud-sdk
```

Expected: PASS.

- [ ] **Step 2: Run package builds**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-platform/shell build
pnpm --dir frontend --filter @icp-modules/organization build
```

Expected: PASS. Vite chunk-size warnings are acceptable if build exits successfully.

- [ ] **Step 3: Update verification record**

Append this section to `docs/verification/frontend-ant-design-rebuild-verification.md`:

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

- [ ] **Step 4: Update handoff**

In `docs/phases/current-handoff.md`, add a “人员中心后台 CRUD Pro 样板固化” item under recent completion and update the current recommendation so the next step is no longer “配置管理” by default.

Use this wording:

```md
### 4.x 人员中心后台 CRUD Pro 样板固化

状态：已完成并验证。

完成内容：

- 人员中心作为平台第一版完整后台 CRUD 样板，覆盖列表、详情抽屉和表单抽屉。
- 新增、查看、编辑默认在右侧抽屉完成，独立详情页保留为深链接和回滚出口。
- 标准动作集收敛为 `create / refresh / view / edit / delete`，`disable` 明确为可选标准动作。
- 人员中心未强制展示删除入口；只有资源显式声明并具备后端能力时才展示删除。
```

- [ ] **Step 5: Run final status check**

Run:

```powershell
git status --short
```

Expected: only intended verification/handoff docs are modified.

- [ ] **Step 6: Commit**

```powershell
git add docs/verification/frontend-ant-design-rebuild-verification.md docs/phases/current-handoff.md
git commit -m "验证：记录人员中心CRUD样板固化结果"
```

## Plan Self-Review

- Spec coverage: Tasks cover UI adapters, Shell Host orchestration, Organization resource cleanup, `create / refresh / view / edit / delete`, optional `disable`, right-side drawers, independent detail fallback, route compatibility, verification records, and handoff.
- Placeholder scan: No `TBD`, `TODO`, or “implement later” placeholders should remain. The only conditional behavior is explicit: person delete is hidden unless the resource declares it and backend support exists.
- Type consistency: `CrudDrawerState`, required/optional actions, `StandardCrudClient.delete`, and UI adapter names are consistent across tasks.
