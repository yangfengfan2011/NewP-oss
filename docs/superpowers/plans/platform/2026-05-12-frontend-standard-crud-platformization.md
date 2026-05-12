# 前端标准 CRUD 平台化第一批 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把人员中心前端的标准 CRUD 列表/详情主流程提升到平台层，同时把表单、lookup、动作和摘要块深层平台化登记为第二批必做范围。

**Architecture:** 第一批在 `@icp-platform/sdk` 中定义标准 CRUD 前端契约和客户端帮助器，在 Shell 中新增标准 CRUD schema resolver、registry、列表 Host 和详情 Host。Organization 作为试点模块只提供人员资源注册、字段/动作/详情扩展，并保留第二批前暂留的人员表单、账号绑定和授权摘要组件。

**Tech Stack:** React 18 / TypeScript / Vite / Vitest / @icp-platform/sdk / @icp-platform/shell / @icp-platform/ui

---

## 文件结构与职责

**Create**

- `frontend/packages/sdk/src/standardCrud.ts`  
  标准 CRUD 前端公共契约、客户端帮助器、字段读取和标准响应规范化函数。
- `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`  
  将 runtime view metadata 解析为标准 CRUD list/detail schema。
- `frontend/packages/shell/src/registries/standardCrudRegistry.ts`  
  从 `FrontendModule.standardCrudResources` 聚合标准 CRUD 前端资源注册，并拒绝重复 viewKey/resourceKey。
- `frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`  
  从标准 CRUD 资源注册生成详情路由入口，并让 Shell 过滤同路径模块 fallback route。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`  
  标准 CRUD 列表 Host，拥有列表查询、加载、错误、空态、表格、分页和基础刷新状态。
- `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`  
  标准 CRUD 详情 Host，拥有按 id 加载、错误、loading、详情刷新和标准 page action 容器。
- `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`  
  通过 Shell vitest 覆盖 SDK 标准 CRUD helper。
- `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`  
  覆盖 schema resolver 与 registry 的静态行为。
- `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`  
  覆盖标准列表 Host 的 metadata、registry、加载、错误和渲染行为。
- `frontend/packages/shell/src/__tests__/standard-crud-detail-host.test.tsx`  
  覆盖标准详情 Host 的 id、metadata、registry、加载、错误和渲染行为。
- `frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx`  
  覆盖标准 CRUD 详情路由生成、权限继承和 fallback route 过滤。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`  
  人员中心标准 CRUD 前端资源注册，承接第一批仍需保留在模块内的筛选控件、列渲染、行操作、详情内容和表单扩展。
- `docs/verification/frontend-standard-crud-platformization-phase-1-verification.md`  
  记录第一批验证结果、暂留 Organization 的第二批项目和下一步入口。

**Modify**

- `frontend/packages/sdk/src/index.ts`  
  导出标准 CRUD 类型与 helper，并给 `FrontendModule` 增加 `standardCrudResources`。
- `frontend/packages/shell/src/pages/StandardListPageHost.tsx`  
  继续承接 `standard-list` runtime entry；优先使用标准 CRUD 资源注册，找不到时回退旧 `standardListViews` 机制。
- `frontend/packages/shell/src/App.tsx`  
  让 Shell 从标准 CRUD 资源注册生成详情路由，并在 fallback routes 中排除同路径模块旧页面。
- `frontend/packages/shell/src/metadata/viewMetadataResolver.ts`  
  补充 detail view resolver 或复用通用 view resolver。
- `frontend/modules/organization/src/index.ts`  
  注册 `standardCrudResources`，将人员中心主入口切到标准 CRUD 前端资源。
- `frontend/modules/organization/src/services/organizationService.ts`  
  移除已平台化的标准 CRUD 类型与通用响应规范化函数，保留公司/部门/岗位 lookup、人员专属请求和人员字段映射。
- `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`  
  改成兼容薄包装，或删除完整列表状态机后委托 `StandardCrudListPageHost`。
- `frontend/modules/organization/src/pages/PersonListPage.tsx`  
  保留为兼容入口，委托标准 CRUD 列表 Host。
- `frontend/modules/organization/src/pages/PersonDetailPage.tsx`  
  改成兼容错误页；正式详情入口由 Shell 标准 CRUD 详情路由接管。
- `frontend/modules/organization/src/pages/PersonCreatePage.tsx`  
  只改必要 import；新建页表单保留到第二批。
- `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`  
  从 SDK 引入 `StandardCrudSummaryBlock`，继续只承载人员专属展示。
- `frontend/packages/shell/src/__tests__/standard-list-page-host.test.tsx`
- `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
- `frontend/packages/shell/src/__tests__/organization-person-center-detail-layout.test.tsx`
- `frontend/packages/shell/src/__tests__/organization-person-create-flow.test.tsx`  
  对齐标准 CRUD Host 接管后的行为。
- `docs/phases/current-handoff.md`  
  第一批收口时登记第二批为推荐下一步，或明确暂缓原因。

**Do Not Modify**

- `server/**`
- `apps/host-api/**`
- `scripts/build.ps1`
- `scripts/test.ps1`
- `frontend/packages/shell/src/components/Layout.tsx`
- `frontend/packages/shell/src/components/Sidebar.tsx`

---

### Task 0: 实施前一致性 Review

**Files:**
- Read: `docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md`
- Read: `docs/architecture/componentization-guardrails.md`
- Read: `docs/developer/frontend-plugin-integration.md`
- Read: `frontend/packages/sdk/src/index.ts`
- Read: `frontend/packages/shell/src/pages/StandardListPageHost.tsx`
- Read: `frontend/modules/organization/src/index.ts`

- [x] **Step 1: 确认工作区没有需要切分的冲突**

Run:

```powershell
git status --short
```

Expected: 允许看到本计划文档、前端标准 CRUD 设计文档或其他用户已有改动；如果存在与本计划同一批前端文件的未提交修改，先阅读对应 diff，不能覆盖用户改动。

- [x] **Step 2: 阅读设计并确认本计划只覆盖第一批**

Run:

```powershell
Select-String -Path .\docs\superpowers\specs\platform\2026-05-12-frontend-standard-crud-platformization-design.md -Pattern "14.1|14.2|14.3|第二批"
```

Expected: 输出包含第一批、第二批和批间防遗忘机制；本次实现只做第 14.1 节，最后通过文档任务登记第 14.2 节。

- [x] **Step 3: 记录一致性 review 结论**

Create a short local note in the task runner summary, not a repository file:

```text
一致性 review 结论：
- 第一批入口：SDK 标准 CRUD 契约、Shell schema/registry/list/detail Host、Organization 人员试点接入。
- 第一批边界：不修改后端；不把人员账号绑定、授权摘要、公司/部门/岗位语义写入 Shell。
- 第二批登记：第一批收口必须更新验证记录和 current-handoff。
```

- [x] **Step 4: Commit**

No commit for Task 0. This task is a pre-flight gate.

---

### Task 1: SDK 标准 CRUD 契约与客户端 Helper

**Files:**
- Create: `frontend/packages/sdk/src/standardCrud.ts`
- Modify: `frontend/packages/sdk/src/index.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`
- Test: `frontend/packages/shell/package.json`

- [x] **Step 1: 写 SDK helper 失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest';
import {
  buildStandardCrudPath,
  createStandardCrudClient,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  readStandardCrudBoolean,
  readStandardCrudString,
} from '@icp-platform/sdk';

describe('standard CRUD SDK helpers', () => {
  it('normalizes list results with stable paging defaults', () => {
    const result = normalizeStandardCrudListResult(
      {
        items: [{ EID: 'Person_1', ENAME: '张三' }],
        total: 7,
        page: 2,
        pageSize: 20,
        sortBy: 'ENAME',
        sortDirection: 'asc',
      },
      (item) => ({ id: readStandardCrudString(item, ['EID']), name: readStandardCrudString(item, ['ENAME']) }),
    );

    expect(result).toEqual({
      items: [{ id: 'Person_1', name: '张三' }],
      total: 7,
      page: 2,
      pageSize: 20,
      sortBy: 'ENAME',
      sortDirection: 'asc',
    });
  });

  it('uses item count as list fallback when paging fields are missing', () => {
    const result = normalizeStandardCrudListResult({ items: [{ EID: 'Person_1' }] }, (item) => item);

    expect(result.total).toBe(1);
    expect(result.page).toBe(1);
    expect(result.pageSize).toBe(1);
  });

  it('normalizes summary blocks and legacy array summaries', () => {
    expect(normalizeStandardCrudSummaryBlock({ status: 'ready', data: ['A'] })).toEqual({
      status: 'ready',
      data: ['A'],
    });
    expect(normalizeStandardCrudSummaryBlock([])).toEqual({
      status: 'notConfigured',
      data: null,
    });
  });

  it('reads primitive fields from unknown records', () => {
    const value = { EID: 'Person_1', IsEnabled: true };

    expect(readStandardCrudString(value, ['EID'])).toBe('Person_1');
    expect(readStandardCrudString(value, ['Missing'], 'fallback')).toBe('fallback');
    expect(readStandardCrudBoolean(value, ['IsEnabled'], false)).toBe(true);
    expect(readStandardCrudBoolean(value, ['Missing'], true)).toBe(true);
  });

  it('builds API paths with query parameters', () => {
    expect(buildStandardCrudPath('/api/organization/persons', { keyword: 'zhang', empty: '', ignored: null })).toBe(
      '/api/organization/persons?keyword=zhang',
    );
  });

  it('creates a fetch-backed standard CRUD client', async () => {
    const fetcher = vi.fn(async () =>
      new Response(JSON.stringify({ fields: { EID: 'Person_1' }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const client = createStandardCrudClient('/api/organization/persons', fetcher);

    const detail = await client.getDetail('Person_1');

    expect(fetcher).toHaveBeenCalledWith('/api/organization/persons/Person_1', { method: 'GET' });
    expect(detail.fields).toEqual({ EID: 'Person_1' });
  });
});
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-sdk.test.ts
```

Expected: FAIL，提示 `@icp-platform/sdk` 尚未导出 `buildStandardCrudPath` 或其他标准 CRUD helper。

- [x] **Step 3: 新增 SDK 标准 CRUD 实现**

Create `frontend/packages/sdk/src/standardCrud.ts`:

```ts
import type { ComponentType, Dispatch, ReactNode, SetStateAction } from 'react';

export type StandardCrudSummaryStatus = 'ready' | 'notConfigured' | 'dependencyUnavailable';

export interface StandardCrudSummaryBlock<TData = unknown> {
  status: StandardCrudSummaryStatus;
  data: TData | null;
}

export interface StandardCrudListResult<TItem> {
  items: TItem[];
  total: number;
  page: number;
  pageSize: number;
  sortBy?: string | null;
  sortDirection?: string | null;
}

export interface StandardCrudDetail {
  fields: Record<string, unknown>;
  summaries: Record<string, StandardCrudSummaryBlock<unknown> | undefined>;
}

export type StandardCrudMutationValues = Record<string, unknown>;

export interface StandardCrudTableColumn<TRow> {
  key: string;
  title: string;
  render?: (item: TRow) => ReactNode;
}

export interface StandardCrudListExtensionContext<TFilter, TRow> {
  filters: TFilter;
  setFilters: Dispatch<SetStateAction<TFilter>>;
  applyFilters: () => void;
  resetFilters: () => void;
  refresh: () => void;
  loading: boolean;
  rows: TRow[];
  total: number;
}

export interface StandardCrudDetailExtensionContext<TDetail> {
  id: string;
  detail: TDetail;
  refresh: () => void;
  loading: boolean;
}

export interface StandardCrudResourceRegistration<
  TFilter = Record<string, string>,
  TRow = Record<string, unknown>,
  TDetail = StandardCrudDetail,
> {
  resourceKey: string;
  apiRoute: string;
  listViewKey: string;
  detailViewKey?: string;
  detailRoutePath?: string;
  permissions?: string[];
  createEmptyFilters: () => TFilter;
  buildListQuery: (filters: TFilter) => Record<string, string | number | boolean | null | undefined>;
  mapListItem: (value: unknown) => TRow;
  getRowId: (row: TRow) => string;
  buildColumns: (context: StandardCrudListExtensionContext<TFilter, TRow>) => StandardCrudTableColumn<TRow>[];
  renderFilters?: (context: StandardCrudListExtensionContext<TFilter, TRow>) => ReactNode;
  renderHeaderActions?: (context: StandardCrudListExtensionContext<TFilter, TRow>) => ReactNode;
  renderRowActions?: (row: TRow, context: StandardCrudListExtensionContext<TFilter, TRow>) => ReactNode;
  mapDetail?: (detail: StandardCrudDetail) => TDetail;
  renderDetail?: (context: StandardCrudDetailExtensionContext<TDetail>) => ReactNode;
  renderDetailHeaderActions?: (context: StandardCrudDetailExtensionContext<TDetail>) => ReactNode;
}

export interface StandardCrudClient {
  list: (query?: Record<string, string | number | boolean | null | undefined>) => Promise<unknown>;
  getDetail: (id: string) => Promise<StandardCrudDetail>;
  create: (values: StandardCrudMutationValues) => Promise<StandardCrudDetail>;
  update: (id: string, values: StandardCrudMutationValues) => Promise<StandardCrudDetail>;
  disable: (id: string) => Promise<StandardCrudDetail>;
}

export function isStandardCrudRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function readStandardCrudString(value: unknown, keys: string[], fallback = ''): string {
  const record = isStandardCrudRecord(value) ? value : {};
  for (const key of keys) {
    const candidate = record[key];
    if (typeof candidate === 'string' && candidate.trim().length > 0) {
      return candidate;
    }
  }

  return fallback;
}

export function readStandardCrudNullableString(value: unknown, keys: string[]): string | null {
  const record = isStandardCrudRecord(value) ? value : {};
  for (const key of keys) {
    const candidate = record[key];
    if (typeof candidate === 'string' && candidate.trim().length > 0) {
      return candidate;
    }
    if (candidate === null) {
      return null;
    }
  }

  return null;
}

export function readStandardCrudBoolean(value: unknown, keys: string[], fallback = false): boolean {
  const record = isStandardCrudRecord(value) ? value : {};
  for (const key of keys) {
    const candidate = record[key];
    if (typeof candidate === 'boolean') {
      return candidate;
    }
  }

  return fallback;
}

export function normalizeStandardCrudSummaryBlock<TData = unknown>(
  value: unknown,
  mapData?: (data: unknown) => TData,
): StandardCrudSummaryBlock<TData> {
  if (isStandardCrudRecord(value)) {
    const status = value.status;
    if (status === 'ready' || status === 'notConfigured' || status === 'dependencyUnavailable') {
      const rawData = value.data;
      return {
        status,
        data: rawData == null ? null : mapData ? mapData(rawData) : (rawData as TData),
      };
    }
  }

  if (Array.isArray(value)) {
    return {
      status: value.length > 0 ? 'ready' : 'notConfigured',
      data: value.length > 0 ? (mapData ? mapData(value) : (value as TData)) : null,
    };
  }

  if (value == null) {
    return { status: 'notConfigured', data: null };
  }

  return {
    status: 'ready',
    data: mapData ? mapData(value) : (value as TData),
  };
}

export function normalizeStandardCrudDetail(value: unknown): StandardCrudDetail {
  const record = isStandardCrudRecord(value) ? value : {};
  const fields = isStandardCrudRecord(record.fields) ? record.fields : {};
  const rawSummaries = isStandardCrudRecord(record.summaries) ? record.summaries : {};
  const summaries: StandardCrudDetail['summaries'] = {};

  for (const [key, summary] of Object.entries(rawSummaries)) {
    summaries[key] = normalizeStandardCrudSummaryBlock(summary);
  }

  return { fields, summaries };
}

export function normalizeStandardCrudListResult<TItem>(
  value: unknown,
  mapItem: (value: unknown) => TItem,
): StandardCrudListResult<TItem> {
  const record = isStandardCrudRecord(value) ? value : {};
  const rawItems = Array.isArray(record.items) ? record.items : [];
  const items = rawItems.map(mapItem);

  return {
    items,
    total: typeof record.total === 'number' ? record.total : items.length,
    page: typeof record.page === 'number' ? record.page : 1,
    pageSize: typeof record.pageSize === 'number' ? record.pageSize : items.length,
    sortBy: typeof record.sortBy === 'string' ? record.sortBy : null,
    sortDirection: typeof record.sortDirection === 'string' ? record.sortDirection : null,
  };
}

export function buildStandardCrudPath(
  apiRoute: string,
  query?: Record<string, string | number | boolean | null | undefined>,
): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value === null || value === undefined || value === '') {
      continue;
    }

    search.set(key, String(value));
  }

  const suffix = search.toString();
  return suffix.length > 0 ? `${apiRoute}?${suffix}` : apiRoute;
}

async function requestJson<T>(fetcher: typeof fetch, path: string, init: RequestInit, errorMessage: string): Promise<T> {
  const response = await fetcher(path, init);
  if (!response.ok) {
    throw new Error(errorMessage);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export function createStandardCrudClient(apiRoute: string, fetcher: typeof fetch = fetch): StandardCrudClient {
  return {
    list: (query) => requestJson<unknown>(fetcher, buildStandardCrudPath(apiRoute, query), { method: 'GET' }, '加载标准列表失败'),
    getDetail: (id) => requestJson<StandardCrudDetail>(fetcher, `${apiRoute}/${encodeURIComponent(id)}`, { method: 'GET' }, '加载标准详情失败'),
    create: async (values) => normalizeStandardCrudDetail(await requestJson<unknown>(
      fetcher,
      apiRoute,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(values),
      },
      '创建标准资源失败',
    )),
    update: async (id, values) => normalizeStandardCrudDetail(await requestJson<unknown>(
      fetcher,
      `${apiRoute}/${encodeURIComponent(id)}`,
      {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(values),
      },
      '保存标准资源失败',
    )),
    disable: async (id) => normalizeStandardCrudDetail(await requestJson<unknown>(
      fetcher,
      `${apiRoute}/${encodeURIComponent(id)}/disable`,
      { method: 'POST' },
      '停用标准资源失败',
    )),
  };
}
```

第一批暂不新增 `StandardCrudFieldRenderer`、`StandardCrudActionHandler`、`StandardCrudSummaryBlockRenderer` 等命名 renderer 类型；本计划先用 `StandardCrudResourceRegistration` 上的受控 renderer 回调承接列表、详情和模块专属动作，深层字段渲染、动作协议和 summary block renderer 在第二批固化。

- [x] **Step 4: 从 SDK 入口导出标准 CRUD 契约**

Modify `frontend/packages/sdk/src/index.ts`:

```ts
export type {
  StandardCrudClient,
  StandardCrudDetail,
  StandardCrudDetailExtensionContext,
  StandardCrudListExtensionContext,
  StandardCrudListResult,
  StandardCrudMutationValues,
  StandardCrudResourceRegistration,
  StandardCrudSummaryBlock,
  StandardCrudSummaryStatus,
  StandardCrudTableColumn,
} from './standardCrud';

export {
  buildStandardCrudPath,
  createStandardCrudClient,
  isStandardCrudRecord,
  normalizeStandardCrudDetail,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
} from './standardCrud';
```

Then add `standardCrudResources` to `FrontendModule`:

```ts
export interface FrontendModule {
  metadata: ModuleMetadata;
  standardCrudResources?: StandardCrudResourceRegistration[];
  standardListViews?: StandardListViewRegistration[];
  customPages?: CustomPageRegistration[];
  routes?: RouteConfig[];
  menus?: MenuConfig[];
  permissions?: PermissionDeclaration[];
  i18n?: I18nResource[];
  initialize?: () => void | Promise<void>;
  cleanup?: () => void | Promise<void>;
}
```

- [x] **Step 5: 运行 SDK helper 测试确认通过**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-sdk.test.ts
```

Expected: PASS，`standard-crud-sdk.test.ts` 全部通过。

- [x] **Step 6: 构建 SDK**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
```

Expected: PASS，无 TypeScript 错误。

- [x] **Step 7: Commit**

```powershell
git add .\frontend\packages\sdk\src\standardCrud.ts .\frontend\packages\sdk\src\index.ts .\frontend\packages\shell\src\__tests__\standard-crud-sdk.test.ts
git commit -m "功能：新增前端标准CRUD契约"
```

---

### Task 2: 标准 CRUD Schema Resolver 与 Registry

**Files:**
- Create: `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`
- Modify: `frontend/packages/shell/src/metadata/viewMetadataResolver.ts`
- Create: `frontend/packages/shell/src/registries/standardCrudRegistry.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`

- [x] **Step 1: 写 schema 与 registry 失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import { resolveDetailViewDocument } from '../metadata/viewMetadataResolver';
import {
  buildStandardCrudDetailSchema,
  buildStandardCrudListSchema,
} from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';

const personRegistration = {
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
  listViewKey: 'organization.person.list',
  detailViewKey: 'organization.person.detail',
  createEmptyFilters: () => ({ keyword: '' }),
  buildListQuery: (filters: { keyword: string }) => filters,
  mapListItem: (value: unknown) => value,
  getRowId: () => 'row-1',
  buildColumns: () => [],
};

describe('standard CRUD schema resolver and registry', () => {
  it('builds a list schema from runtime view metadata and registration', () => {
    const schema = buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        query: {
          fields: [{ key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword', operator: 'like' }],
        },
        columns: [{ key: 'name', labelKey: 'Column_Name' }],
        pageActions: [{ key: 'refresh', labelKey: 'Action_Refresh' }],
        rowActions: [{ key: 'view', labelKey: 'Action_View' }],
        export: { modes: ['current'] },
      },
      personRegistration,
    );

    expect(schema).toMatchObject({
      viewKey: 'organization.person.list',
      resourceKey: 'organization.person',
      apiRoute: '/api/organization/persons',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      detailViewKey: 'organization.person.detail',
    });
    expect(schema.queryFields).toHaveLength(1);
    expect(schema.columns).toEqual([{ key: 'name', labelKey: 'Column_Name' }]);
    expect(schema.exportModes).toEqual(['current']);
  });

  it('builds a detail schema from runtime view metadata and registration', () => {
    const schema = buildStandardCrudDetailSchema(
      {
        key: 'organization.person.detail',
        dicKey: 'PersonDetailView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'detail',
        sections: ['basic', 'organization'],
        summaryBlocks: ['userLink'],
        pageActions: [{ key: 'disable', labelKey: 'Action_Disable' }],
      },
      personRegistration,
    );

    expect(schema).toMatchObject({
      viewKey: 'organization.person.detail',
      resourceKey: 'organization.person',
      apiRoute: '/api/organization/persons',
      dicKey: 'PersonDetailView',
      titleKey: 'Title',
    });
    expect(schema.sections).toEqual(['basic', 'organization']);
    expect(schema.summaryBlocks).toEqual(['userLink']);
  });

  it('resolves only detail view documents for detail mode', () => {
    const detail = resolveDetailViewDocument(
      [
        { type: 'view', view: { key: 'organization.person.list', mode: 'list' } },
        { type: 'view', view: { key: 'organization.person.detail', mode: 'detail' } },
      ],
      'organization.person.detail',
    );

    expect(detail?.view.key).toBe('organization.person.detail');
  });

  it('indexes resources by resource key, list view key and detail view key', () => {
    const registry = buildStandardCrudRegistry([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [personRegistration],
      } satisfies FrontendModule,
    ]);

    expect(registry.findByResourceKey('organization.person')).toBe(personRegistration);
    expect(registry.findByListViewKey('organization.person.list')).toBe(personRegistration);
    expect(registry.findByDetailViewKey('organization.person.detail')).toBe(personRegistration);
  });

  it('drops duplicate resource registrations and reports the conflict', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const registry = buildStandardCrudRegistry([
      {
        metadata: { moduleId: 'organization-a', displayName: 'A', version: '1.0.0' },
        standardCrudResources: [personRegistration],
      },
      {
        metadata: { moduleId: 'organization-b', displayName: 'B', version: '1.0.0' },
        standardCrudResources: [{ ...personRegistration, apiRoute: '/api/other' }],
      },
    ]);

    expect(errorSpy).toHaveBeenCalledTimes(1);
    expect(registry.findByResourceKey('organization.person')).toBeUndefined();
    expect(registry.findByListViewKey('organization.person.list')).toBeUndefined();

    errorSpy.mockRestore();
  });
});
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry.test.ts
```

Expected: FAIL，提示 `standardCrudSchemaResolver`、`standardCrudRegistry` 或 `resolveDetailViewDocument` 不存在。

- [x] **Step 3: 实现 detail view resolver**

Modify `frontend/packages/shell/src/metadata/viewMetadataResolver.ts`:

```ts
import type { RuntimeViewDocument } from './runtimeMetadataManifest';

type RuntimeViewShape = {
  key?: string;
  mode?: string;
  type?: string;
};

function resolveViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
  expectedMode: 'list' | 'detail',
): RuntimeViewDocument | null {
  return (
    viewDocuments.find((document) => {
      const view = document.view as RuntimeViewShape;
      const resolvedType = view.mode !== undefined ? view.mode : view.type;

      return view.key === viewKey && (resolvedType === undefined || resolvedType === expectedMode);
    }) ?? null
  );
}

export function resolveListViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
): RuntimeViewDocument | null {
  return resolveViewDocument(viewDocuments, viewKey, 'list');
}

export function resolveDetailViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
): RuntimeViewDocument | null {
  return resolveViewDocument(viewDocuments, viewKey, 'detail');
}
```

- [x] **Step 4: 实现标准 CRUD schema resolver**

Create `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`:

```ts
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export interface StandardCrudRuntimeFieldSchema {
  key: string;
  labelKey: string;
  source: 'preset' | 'model';
  field?: string;
  preset?: string;
  operator?: string;
}

export interface StandardCrudRuntimeActionSchema {
  key: string;
  labelKey: string;
}

export interface StandardCrudRuntimeColumnSchema {
  key: string;
  labelKey: string;
}

export interface StandardCrudListRuntimeSchema {
  viewKey: string;
  resourceKey: string;
  apiRoute: string;
  dicKey: string;
  titleKey: string;
  descriptionKey: string;
  editorMode: 'drawer' | 'dialog' | 'page';
  detailViewKey?: string;
  queryFields: StandardCrudRuntimeFieldSchema[];
  columns: StandardCrudRuntimeColumnSchema[];
  pageActions: StandardCrudRuntimeActionSchema[];
  rowActions: StandardCrudRuntimeActionSchema[];
  exportModes: string[];
}

export interface StandardCrudDetailRuntimeSchema {
  viewKey: string;
  resourceKey: string;
  apiRoute: string;
  dicKey: string;
  titleKey: string;
  descriptionKey: string;
  sections: string[];
  summaryBlocks: string[];
  pageActions: StandardCrudRuntimeActionSchema[];
}

function readString(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
}

function readArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

function readRecord(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function readAction(value: Record<string, unknown>): StandardCrudRuntimeActionSchema {
  return {
    key: readString(value.key, ''),
    labelKey: readString(value.labelKey, ''),
  };
}

function readField(value: Record<string, unknown>): StandardCrudRuntimeFieldSchema {
  return {
    key: readString(value.key, ''),
    labelKey: readString(value.labelKey, ''),
    source: value.source === 'model' ? 'model' : 'preset',
    field: typeof value.field === 'string' ? value.field : undefined,
    preset: typeof value.preset === 'string' ? value.preset : undefined,
    operator: typeof value.operator === 'string' ? value.operator : undefined,
  };
}

export function buildStandardCrudListSchema(
  view: Record<string, unknown>,
  registration: StandardCrudResourceRegistration,
): StandardCrudListRuntimeSchema {
  const query = readRecord(view.query);
  const exportConfig = readRecord(view.export);
  const editorMode = view.editorMode === 'page' || view.editorMode === 'dialog' || view.editorMode === 'drawer'
    ? view.editorMode
    : 'drawer';

  return {
    viewKey: readString(view.key, registration.listViewKey),
    resourceKey: registration.resourceKey,
    apiRoute: registration.apiRoute,
    dicKey: readString(view.dicKey, registration.resourceKey),
    titleKey: readString(view.titleKey, 'Title'),
    descriptionKey: readString(view.descriptionKey, 'Description'),
    editorMode,
    detailViewKey: readString(view.detailViewKey, registration.detailViewKey ?? ''),
    queryFields: readArray<Record<string, unknown>>(query.fields).map(readField).filter((field) => field.key.length > 0),
    columns: readArray<Record<string, unknown>>(view.columns).map((column) => ({
      key: readString(column.key, ''),
      labelKey: readString(column.labelKey, ''),
    })).filter((column) => column.key.length > 0),
    pageActions: readArray<Record<string, unknown>>(view.pageActions).map(readAction).filter((action) => action.key.length > 0),
    rowActions: readArray<Record<string, unknown>>(view.rowActions).map(readAction).filter((action) => action.key.length > 0),
    exportModes: readArray<string>(exportConfig.modes).filter((mode): mode is string => typeof mode === 'string'),
  };
}

export function buildStandardCrudDetailSchema(
  view: Record<string, unknown>,
  registration: StandardCrudResourceRegistration,
): StandardCrudDetailRuntimeSchema {
  return {
    viewKey: readString(view.key, registration.detailViewKey ?? ''),
    resourceKey: registration.resourceKey,
    apiRoute: registration.apiRoute,
    dicKey: readString(view.dicKey, registration.resourceKey),
    titleKey: readString(view.titleKey, 'Title'),
    descriptionKey: readString(view.descriptionKey, 'Description'),
    sections: readArray<string>(view.sections).filter((section): section is string => typeof section === 'string'),
    summaryBlocks: readArray<string>(view.summaryBlocks).filter((block): block is string => typeof block === 'string'),
    pageActions: readArray<Record<string, unknown>>(view.pageActions).map(readAction).filter((action) => action.key.length > 0),
  };
}
```

- [x] **Step 5: 实现标准 CRUD registry**

Create `frontend/packages/shell/src/registries/standardCrudRegistry.ts`:

```ts
import type { FrontendModule, StandardCrudResourceRegistration } from '@icp-platform/sdk';

export interface StandardCrudRegistry {
  findByResourceKey: (resourceKey: string) => StandardCrudResourceRegistration | undefined;
  findByListViewKey: (viewKey: string) => StandardCrudResourceRegistration | undefined;
  findByDetailViewKey: (viewKey: string) => StandardCrudResourceRegistration | undefined;
}

export function buildStandardCrudRegistry(modules: FrontendModule[]): StandardCrudRegistry {
  const byResourceKey = new Map<string, StandardCrudResourceRegistration>();
  const byListViewKey = new Map<string, StandardCrudResourceRegistration>();
  const byDetailViewKey = new Map<string, StandardCrudResourceRegistration>();
  const duplicateResourceKeys = new Set<string>();
  const duplicateViewKeys = new Set<string>();

  for (const module of modules) {
    for (const resource of module.standardCrudResources ?? []) {
      if (byResourceKey.has(resource.resourceKey)) {
        duplicateResourceKeys.add(resource.resourceKey);
      } else {
        byResourceKey.set(resource.resourceKey, resource);
      }

      if (byListViewKey.has(resource.listViewKey)) {
        duplicateViewKeys.add(resource.listViewKey);
      } else {
        byListViewKey.set(resource.listViewKey, resource);
      }

      if (resource.detailViewKey) {
        if (byDetailViewKey.has(resource.detailViewKey)) {
          duplicateViewKeys.add(resource.detailViewKey);
        } else {
          byDetailViewKey.set(resource.detailViewKey, resource);
        }
      }
    }
  }

  for (const resourceKey of duplicateResourceKeys) {
    console.error(`Duplicate standard CRUD resource registration detected for resourceKey "${resourceKey}". The key is ignored.`);
    byResourceKey.delete(resourceKey);
  }

  for (const viewKey of duplicateViewKeys) {
    console.error(`Duplicate standard CRUD view registration detected for viewKey "${viewKey}". The key is ignored.`);
    byListViewKey.delete(viewKey);
    byDetailViewKey.delete(viewKey);
  }

  return {
    findByResourceKey: (resourceKey) => byResourceKey.get(resourceKey),
    findByListViewKey: (viewKey) => byListViewKey.get(viewKey),
    findByDetailViewKey: (viewKey) => byDetailViewKey.get(viewKey),
  };
}
```

- [x] **Step 6: 运行 schema 与 registry 测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry.test.ts
```

Expected: PASS。

- [x] **Step 7: 构建 Shell 类型检查**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell build
```

Expected: PASS。

- [x] **Step 8: Commit**

```powershell
git add .\frontend\packages\shell\src\metadata\viewMetadataResolver.ts .\frontend\packages\shell\src\metadata\standardCrudSchemaResolver.ts .\frontend\packages\shell\src\registries\standardCrudRegistry.ts .\frontend\packages\shell\src\__tests__\standard-crud-schema-and-registry.test.ts
git commit -m "功能：新增前端标准CRUD注册解析"
```

---

### Task 3: 标准 CRUD 列表 Host

**Files:**
- Create: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardListPageHost.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-list-page-host.test.tsx`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`

- [x] **Step 1: 写列表 Host 失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`:

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { ModuleRegistry } from '@icp-platform/sdk';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('StandardCrudListPageHost', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  async function renderHost() {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            modelDocuments: [],
            viewDocuments: [
              {
                type: 'view',
                view: {
                  key: 'organization.person.list',
                  dicKey: 'PersonListView',
                  titleKey: 'Title',
                  descriptionKey: 'Description',
                  mode: 'list',
                  columns: [{ key: 'name', labelKey: 'Column_Name' }],
                  pageActions: [{ key: 'refresh', labelKey: 'Action_Refresh' }],
                },
              },
            ],
          }}
        >
          <StandardCrudListPageHost viewKey="organization.person.list" />
        </RuntimeMetadataProvider>,
      );
    });
  }

  it('loads rows through standard CRUD registration', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            createEmptyFilters: () => ({ keyword: '' }),
            buildListQuery: (filters: { keyword: string }) => filters,
            mapListItem: (value: unknown) => value as { id: string; name: string },
            getRowId: (row: { id: string }) => row.id,
            buildColumns: () => [{ key: 'name', title: '姓名' }],
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [{ id: 'Person_1', name: '张三' }], total: 1, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await act(async () => undefined);

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons', { method: 'GET' });
    expect(container.textContent).toContain('张三');
    expect(container.textContent).toContain('共 1 条');
  });

  it('renders a clear error when registration is missing', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([]);

    await renderHost();

    expect(container.textContent).toContain('未找到标准 CRUD 前端资源注册项');
  });
});
```

- [x] **Step 2: 运行列表 Host 测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-list-host.test.tsx
```

Expected: FAIL，提示 `StandardCrudListPageHost` 不存在。

- [x] **Step 3: 实现标准 CRUD 列表 Host**

Create `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`:

```tsx
import { useEffect, useMemo, useState, type ReactElement } from 'react';
import { ModuleRegistry, createStandardCrudClient, normalizeStandardCrudListResult } from '@icp-platform/sdk';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { Button, EmptyState, ErrorState, LoadingState, Panel, StandardListPage, TableWrap } from '@icp-platform/ui';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';

interface StandardCrudListPageHostProps {
  viewKey: string;
}

export default function StandardCrudListPageHost({ viewKey }: StandardCrudListPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveListViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByListViewKey(viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">未找到列表视图元数据</div>;
  }

  if (!registration) {
    return <div className="sh-error-state">未找到标准 CRUD 前端资源注册项</div>;
  }

  return <StandardCrudListPageContent view={viewDocument.view} registration={registration} />;
}

function StandardCrudListPageContent<TFilter, TRow>({
  view,
  registration,
}: {
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<TFilter, TRow>;
}) {
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const [filters, setFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [rows, setRows] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadTick, setReloadTick] = useState(0);

  const context = {
    filters,
    setFilters,
    applyFilters: () => setAppliedFilters(filters),
    resetFilters: () => {
      const empty = registration.createEmptyFilters();
      setFilters(empty);
      setAppliedFilters(empty);
    },
    refresh: () => setReloadTick((current) => current + 1),
    loading,
    rows,
    total,
  };

  useEffect(() => {
    let cancelled = false;

    async function loadRows() {
      try {
        setLoading(true);
        setError(null);
        const rawResult = await client.list(registration.buildListQuery(appliedFilters));
        const result = normalizeStandardCrudListResult(rawResult, registration.mapListItem);

        if (!cancelled) {
          setRows(result.items);
          setTotal(result.total);
        }
      } catch (loadError) {
        if (!cancelled) {
          setRows([]);
          setTotal(0);
          setError(loadError instanceof Error ? loadError.message : '加载标准 CRUD 列表失败');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadRows();

    return () => {
      cancelled = true;
    };
  }, [client, registration, appliedFilters, reloadTick]);

  const filterBar = (
    <section className="sh-standard-list-page-filter">
      {registration.renderFilters ? registration.renderFilters(context) : null}
      <div style={{ marginTop: 10, display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
        <Button size="sm" onClick={context.resetFilters}>重置</Button>
        <Button size="sm" variant="primary" onClick={context.applyFilters} disabled={loading}>
          {loading ? '搜索中...' : '搜索'}
        </Button>
      </div>
    </section>
  );

  const columns = registration.renderRowActions
    ? [
      ...registration.buildColumns(context),
      {
        key: 'actions',
        title: '操作',
        render: (row: TRow) => registration.renderRowActions?.(row, context),
      },
    ]
    : registration.buildColumns(context);

  const content = (
    <Panel
      title="列表"
      compact
      actions={registration.renderHeaderActions ? registration.renderHeaderActions(context) : (
        <Button size="sm" variant="primary" onClick={context.refresh} disabled={loading}>
          {loading ? '刷新中...' : '刷新'}
        </Button>
      )}
    >
      {error ? <ErrorState message={error} /> : null}
      {loading ? (
        <LoadingState message="正在加载列表..." />
      ) : rows.length === 0 ? (
        <EmptyState message="暂无符合筛选条件的数据。" />
      ) : (
        <TableWrap columns={columns} data={rows} rowKey={registration.getRowId} />
      )}
    </Panel>
  );

  const footerRight = (
    <>
      <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 14 }}>{`共 ${total} 条`}</span>
      <Button size="sm" disabled>上一页</Button>
      <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 14 }}>第 1 / 1 页</span>
      <Button size="sm" disabled>下一页</Button>
    </>
  );

  return (
    <StandardListPage
      title={schema.titleKey}
      description={schema.descriptionKey}
      headerActions={registration.renderHeaderActions ? registration.renderHeaderActions(context) : undefined}
      filterBar={filterBar}
      content={content}
      footerRight={footerRight}
    />
  );
}
```

- [x] **Step 4: 让原 StandardListPageHost 优先使用标准 CRUD Host**

Modify `frontend/packages/shell/src/pages/StandardListPageHost.tsx`:

```tsx
import type { ReactElement } from 'react';
import { StandardListPage } from '@icp-platform/ui';
import { ModuleRegistry } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildListViewRegistry } from '../registries/listViewRegistry';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import StandardCrudListPageHost from './StandardCrudListPageHost';

interface StandardListPageHostProps {
  viewKey: string;
}

export default function StandardListPageHost({ viewKey }: StandardListPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocuments = runtimeMetadata?.viewDocuments ?? [];
  const viewDocument = resolveListViewDocument(viewDocuments, viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">未找到列表视图元数据</div>;
  }

  const modules = ModuleRegistry.getAllModules();
  const standardCrudRegistration = buildStandardCrudRegistry(modules).findByListViewKey(viewKey);

  if (standardCrudRegistration) {
    return <StandardCrudListPageHost viewKey={viewKey} />;
  }

  // 旧 standardListViews 作为兼容机制保留；新标准 CRUD 资源优先通过 standardCrudResources 接入。
  const registration = buildListViewRegistry(modules).get(viewKey);

  if (!registration) {
    return <div className="sh-error-state">未找到列表页注册项</div>;
  }

  const EntryComponent = registration.component;

  return (
    <EntryComponent
      viewKey={viewKey}
      viewMetadata={viewDocument.view as Record<string, unknown>}
    >
      {(definition) => <StandardListPage {...definition} />}
    </EntryComponent>
  );
}
```

- [x] **Step 5: 更新原 StandardListPageHost 测试**

Modify `frontend/packages/shell/src/__tests__/standard-list-page-host.test.tsx` by adding one test:

```tsx
it('prefers standard CRUD resource registrations over legacy list registrations', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response(JSON.stringify({ items: [{ id: 'Person_1', name: '张三' }], total: 1, page: 1, pageSize: 20 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  );
  vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
    {
      metadata: {
        moduleId: 'organization',
        displayName: 'Organization',
        version: '1.0.0',
      },
      standardCrudResources: [
        {
          resourceKey: 'organization.person',
          apiRoute: '/api/organization/persons',
          listViewKey: 'organization.person.list',
          detailViewKey: 'organization.person.detail',
          createEmptyFilters: () => ({ keyword: '' }),
          buildListQuery: (filters: { keyword: string }) => filters,
          mapListItem: (value: unknown) => value as { id: string; name: string },
          getRowId: (row: { id: string }) => row.id,
          buildColumns: () => [{ key: 'name', title: '姓名' }],
        },
      ],
      standardListViews: [
        {
          viewKey: 'organization.person.list',
          component: () => <div>旧列表</div>,
        },
      ],
    },
  ]);

  await renderHost([
    {
      type: 'view',
      view: {
        key: 'organization.person.list',
        mode: 'list',
        title: '人员中心',
      },
    },
  ]);
  await act(async () => undefined);

  expect(container.textContent).toContain('张三');
  expect(container.textContent).not.toContain('旧列表');
});
```

- [x] **Step 6: 运行列表 Host 测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-list-host.test.tsx standard-list-page-host.test.tsx
```

Expected: PASS。

- [x] **Step 7: Commit**

```powershell
git add .\frontend\packages\shell\src\pages\StandardCrudListPageHost.tsx .\frontend\packages\shell\src\pages\StandardListPageHost.tsx .\frontend\packages\shell\src\__tests__\standard-crud-list-host.test.tsx .\frontend\packages\shell\src\__tests__\standard-list-page-host.test.tsx
git commit -m "功能：新增前端标准CRUD列表宿主"
```

---

### Task 4: 标准 CRUD 详情 Host 与 Shell 路由接管

**Files:**
- Create: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- Create: `frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`
- Modify: `frontend/packages/shell/src/App.tsx`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-detail-host.test.tsx`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx`

- [x] **Step 1: 写详情 Host 失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-detail-host.test.tsx`:

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { ModuleRegistry } from '@icp-platform/sdk';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('StandardCrudDetailPageHost', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();

    if (root) {
      await act(async () => root.unmount());
    }

    container?.remove();
  });

  async function renderHost(id: string | null) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            modelDocuments: [],
            viewDocuments: [
              {
                type: 'view',
                view: {
                  key: 'organization.person.detail',
                  dicKey: 'PersonDetailView',
                  titleKey: 'Title',
                  descriptionKey: 'Description',
                  mode: 'detail',
                  sections: ['basic'],
                  summaryBlocks: ['userLink'],
                },
              },
            ],
          }}
        >
          <StandardCrudDetailPageHost viewKey="organization.person.detail" id={id} />
        </RuntimeMetadataProvider>,
      );
    });
  }

  it('loads detail through standard CRUD registration', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            buildColumns: () => [],
            mapDetail: (detail) => ({ id: String(detail.fields.EID), name: String(detail.fields.ENAME) }),
            renderDetail: ({ detail }) => <div data-testid="detail">{(detail as { name: string }).name}</div>,
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ fields: { EID: 'Person_1', ENAME: '张三' }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost('Person_1');
    await act(async () => undefined);

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons/Person_1', { method: 'GET' });
    expect(container.textContent).toContain('张三');
  });

  it('renders a stable error when id is missing', async () => {
    await renderHost(null);

    expect(container.textContent).toContain('当前地址中没有有效的资源标识');
  });
});
```

- [x] **Step 2: 写标准 CRUD 路由 adapter 失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-route-adapter.test.tsx`:

```tsx
import { describe, expect, it } from 'vitest';
import type { FrontendModule, RouteConfig } from '@icp-platform/sdk';
import {
  buildStandardCrudDetailRouteEntries,
  filterFallbackRoutesByStandardCrudRoutes,
} from '../navigation/standardCrudRouteAdapter';

const modules: FrontendModule[] = [
  {
    metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
    standardCrudResources: [
      {
        resourceKey: 'organization.person',
        apiRoute: '/api/organization/persons',
        listViewKey: 'organization.person.list',
        detailViewKey: 'organization.person.detail',
        detailRoutePath: '/organization/persons/:id',
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value: unknown) => value,
        getRowId: () => 'row-1',
        buildColumns: () => [],
      },
    ],
  },
];

const fallbackRoutes: RouteConfig[] = [
  { path: '/organization/persons', component: () => null, permissions: ['Organization.Person.View'] },
  { path: '/organization/persons/:id', component: () => null, permissions: ['Organization.Person.View'] },
];

describe('standard CRUD route adapter', () => {
  it('builds detail route entries and inherits module route permissions', () => {
    const entries = buildStandardCrudDetailRouteEntries(modules, fallbackRoutes);

    expect(entries).toEqual([
      {
        path: '/organization/persons/:id',
        viewKey: 'organization.person.detail',
        permissions: ['Organization.Person.View'],
      },
    ]);
  });

  it('removes module fallback routes owned by standard CRUD detail routes', () => {
    const entries = buildStandardCrudDetailRouteEntries(modules, fallbackRoutes);

    expect(filterFallbackRoutesByStandardCrudRoutes(fallbackRoutes, entries).map((route) => route.path)).toEqual([
      '/organization/persons',
    ]);
  });
});
```

- [x] **Step 3: 运行详情与路由测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-detail-host.test.tsx standard-crud-route-adapter.test.tsx
```

Expected: FAIL，提示 `StandardCrudDetailPageHost` 与 `standardCrudRouteAdapter` 不存在。

- [x] **Step 4: 实现标准 CRUD 详情 Host**

Create `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`:

```tsx
import { useEffect, useMemo, useState, type ReactElement } from 'react';
import {
  ModuleRegistry,
  createStandardCrudClient,
  normalizeStandardCrudDetail,
  type StandardCrudDetail,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { ErrorState, LoadingState, PageHeader } from '@icp-platform/ui';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { buildStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveDetailViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';

interface StandardCrudDetailPageHostProps {
  viewKey: string;
  id: string | null;
}

export default function StandardCrudDetailPageHost({ viewKey, id }: StandardCrudDetailPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveDetailViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByDetailViewKey(viewKey);

  if (!id) {
    return <ErrorState message="当前地址中没有有效的资源标识。" />;
  }

  if (!viewDocument) {
    return <div className="sh-error-state">未找到详情视图元数据</div>;
  }

  if (!registration) {
    return <div className="sh-error-state">未找到标准 CRUD 前端资源注册项</div>;
  }

  return <StandardCrudDetailPageContent id={id} view={viewDocument.view} registration={registration} />;
}

function StandardCrudDetailPageContent<TDetail>({
  id,
  view,
  registration,
}: {
  id: string;
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<Record<string, string>, Record<string, unknown>, TDetail>;
}) {
  const schema = useMemo(() => buildStandardCrudDetailSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const [detail, setDetail] = useState<TDetail | StandardCrudDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadTick, setReloadTick] = useState(0);

  useEffect(() => {
    let cancelled = false;

    async function loadDetail() {
      try {
        setLoading(true);
        setError(null);
        const rawDetail = await client.getDetail(id);
        const normalized = normalizeStandardCrudDetail(rawDetail);
        const mapped = registration.mapDetail ? registration.mapDetail(normalized) : normalized;
        if (!cancelled) {
          setDetail(mapped);
        }
      } catch (loadError) {
        if (!cancelled) {
          setDetail(null);
          setError(loadError instanceof Error ? loadError.message : '加载标准 CRUD 详情失败');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadDetail();

    return () => {
      cancelled = true;
    };
  }, [client, id, registration, reloadTick]);

  const refresh = () => setReloadTick((current) => current + 1);

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow={schema.resourceKey}
        title={schema.titleKey}
        description={schema.descriptionKey}
        actions={detail && registration.renderDetailHeaderActions
          ? registration.renderDetailHeaderActions({ id, detail: detail as TDetail, refresh, loading })
          : undefined}
      />

      {loading ? (
        <LoadingState message="正在加载详情..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : detail && registration.renderDetail ? (
        registration.renderDetail({ id, detail: detail as TDetail, refresh, loading })
      ) : detail ? (
        <pre>{JSON.stringify(detail, null, 2)}</pre>
      ) : (
        <ErrorState message="未找到详情。" />
      )}
    </div>
  );
}
```

- [x] **Step 5: 实现标准 CRUD 详情路由 adapter**

Create `frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`:

```ts
import type { FrontendModule, RouteConfig } from '@icp-platform/sdk';

export interface StandardCrudDetailRouteEntry {
  path: string;
  viewKey: string;
  permissions?: string[];
}

export function buildStandardCrudDetailRouteEntries(
  modules: FrontendModule[],
  moduleRoutes: RouteConfig[],
): StandardCrudDetailRouteEntry[] {
  const moduleRouteByPath = new Map(moduleRoutes.map((route) => [route.path, route] as const));
  const entries: StandardCrudDetailRouteEntry[] = [];

  for (const module of modules) {
    for (const resource of module.standardCrudResources ?? []) {
      if (!resource.detailRoutePath || !resource.detailViewKey) {
        continue;
      }

      const inheritedPermissions = resource.permissions && resource.permissions.length > 0
        ? resource.permissions
        : moduleRouteByPath.get(resource.detailRoutePath)?.permissions;

      entries.push({
        path: resource.detailRoutePath,
        viewKey: resource.detailViewKey,
        permissions: inheritedPermissions,
      });
    }
  }

  return entries;
}

export function filterFallbackRoutesByStandardCrudRoutes(
  moduleRoutes: RouteConfig[],
  standardCrudRoutes: StandardCrudDetailRouteEntry[],
): RouteConfig[] {
  const standardCrudPaths = new Set(standardCrudRoutes.map((entry) => entry.path));
  return moduleRoutes.filter((route) => !standardCrudPaths.has(route.path));
}
```

- [x] **Step 6: 让 AppRoutes 渲染标准 CRUD 详情路由**

Modify `frontend/packages/shell/src/App.tsx`:

```tsx
import { useParams } from 'react-router-dom';
import {
  buildStandardCrudDetailRouteEntries,
  filterFallbackRoutesByStandardCrudRoutes,
} from './navigation/standardCrudRouteAdapter';
import StandardCrudDetailPageHost from './pages/StandardCrudDetailPageHost';

function StandardCrudDetailRoute({ viewKey }: { viewKey: string }) {
  const params = useParams();
  return <StandardCrudDetailPageHost viewKey={viewKey} id={params.id ?? null} />;
}
```

Inside `AppRoutes`, compute standard CRUD route entries from `ModuleRegistry.getAllModules()` and filter fallback routes:

```tsx
const modules = ModuleRegistry.getAllModules();
const moduleRoutes = ModuleRegistry.getRoutes();
const standardCrudDetailEntries = buildStandardCrudDetailRouteEntries(modules, moduleRoutes);
const routeableMetadataEntries = resolveRouteableMetadataEntries(projectedEntries, moduleRoutes);
const fallbackRoutes = filterFallbackRoutesByStandardCrudRoutes(
  filterFallbackRoutesByMetadataEntries(moduleRoutes, routeableMetadataEntries),
  standardCrudDetailEntries,
);
```

Render `standardCrudDetailEntries` before `fallbackRoutes`:

```tsx
{standardCrudDetailEntries.map((entry) => (
  <Route
    key={entry.path}
    path={entry.path}
    element={entry.permissions && entry.permissions.length > 0 ? (
      <PermissionGuard permissions={entry.permissions}>
        <StandardCrudDetailRoute viewKey={entry.viewKey} />
      </PermissionGuard>
    ) : (
      <StandardCrudDetailRoute viewKey={entry.viewKey} />
    )}
  />
))}
```

- [x] **Step 7: 运行详情与路由测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-detail-host.test.tsx standard-crud-route-adapter.test.tsx
```

Expected: PASS。

- [x] **Step 8: Commit**

```powershell
git add .\frontend\packages\shell\src\pages\StandardCrudDetailPageHost.tsx .\frontend\packages\shell\src\navigation\standardCrudRouteAdapter.ts .\frontend\packages\shell\src\App.tsx .\frontend\packages\shell\src\__tests__\standard-crud-detail-host.test.tsx .\frontend\packages\shell\src\__tests__\standard-crud-route-adapter.test.tsx
git commit -m "功能：新增前端标准CRUD详情路由"
```

---

### Task 5: Organization 人员中心接入第一批平台能力

**Files:**
- Create: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Modify: `frontend/modules/organization/src/index.ts`
- Modify: `frontend/modules/organization/src/services/organizationService.ts`
- Modify: `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`
- Modify: `frontend/modules/organization/src/pages/PersonListPage.tsx`
- Modify: `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- Modify: `frontend/modules/organization/src/pages/PersonCreatePage.tsx`
- Modify: `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- Modify: `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- Test: `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
- Test: `frontend/packages/shell/src/__tests__/organization-person-center-detail-layout.test.tsx`
- Test: `frontend/packages/shell/src/__tests__/organization-person-create-flow.test.tsx`

- [ ] **Step 1: 写人员模块注册回归测试**

Modify `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx` by asserting that Organization registers a standard CRUD resource:

```tsx
import organizationModule from '../../../../modules/organization/src/index';

it('registers person center as a standard CRUD frontend resource', () => {
  const resource = organizationModule.standardCrudResources?.find((item) => item.resourceKey === 'organization.person');

  expect(resource).toMatchObject({
    resourceKey: 'organization.person',
    apiRoute: '/api/organization/persons',
    listViewKey: 'organization.person.list',
    detailViewKey: 'organization.person.detail',
  });
});
```

Use the existing relative import style in this test package when importing Organization code; do not add a new workspace dependency from Shell to `@icp-modules/organization`.

- [ ] **Step 2: 运行人员注册测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- organization-person-center-list-layout.test.tsx
```

Expected: FAIL，提示 `standardCrudResources` 未注册或断言找不到 `organization.person`。

- [ ] **Step 3: 把通用标准 CRUD 类型切到 SDK**

Modify `frontend/modules/organization/src/services/organizationService.ts`:

```ts
import {
  normalizeStandardCrudDetail,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
  type StandardCrudDetail,
  type StandardCrudListResult,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
```

Delete local declarations for:

```ts
export type SummaryBlockStatus = 'ready' | 'notConfigured' | 'dependencyUnavailable';

export interface StandardCrudSummaryBlock<T = unknown> {
  status: SummaryBlockStatus;
  data: T | null;
}

export interface StandardCrudListResult<TItem> {
  items: TItem[];
  total: number;
  page: number;
  pageSize: number;
  sortBy?: string | null;
  sortDirection?: string | null;
}

export interface StandardCrudDetail {
  fields: Record<string, unknown>;
  summaries: Record<string, StandardCrudSummaryBlock<unknown> | undefined>;
}
```

Replace local helper calls:

```ts
const fields = normalizeStandardCrudDetail(value).fields;
const userLinkSummary = normalizeStandardCrudSummaryBlock(summaries['userLink'], mapUserLinkSummary);
const listResult = normalizeStandardCrudListResult(value, normalizePersonListItem);
```

Keep the existing Organization-specific interfaces and service functions in `organizationService.ts`: `CompanyDto`, `DepartmentDto`, `PositionDto`, `PersonUserLinkSummaryData`, `PersonListRow`, `PersonDetailView`, `PersonUpsertRequest`, `employmentStatusOptions`, `getCompanies`, `getDepartments`, `getPositions`, `getPersons`, `getPersonDetail`, `createPerson`, `updatePerson`, and `disablePerson`.

After this step, run:

```powershell
rg -n "export interface StandardCrud|type SummaryBlockStatus|function normalizeSummaryBlock|function normalizePersonListResponse" frontend/modules/organization/src/services/organizationService.ts
```

Expected: no matches.

- [ ] **Step 4: 创建人员标准 CRUD 前端资源注册**

Create `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`:

```tsx
import { useEffect, useMemo, useState } from 'react';
import { Button, ErrorState, Input, Select, StatusBadge } from '@icp-platform/ui';
import type { StandardCrudResourceRegistration, StandardCrudListExtensionContext } from '@icp-platform/sdk';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonEditorForm from '../components/PersonEditorForm';
import PersonUserLinkCard from '../components/PersonUserLinkCard';
import {
  createPerson,
  disablePerson,
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getEmploymentStatusLabel,
  updatePerson,
  type CompanyDto,
  type DepartmentDto,
  type PersonDetailView,
  type PersonListRow,
  type PersonUpsertRequest,
} from '../services/organizationService';

interface PersonListFilters {
  keyword: string;
  companyId: string;
  departmentId: string;
  employmentStatus: string;
}

function createEmptyFilters(): PersonListFilters {
  return {
    keyword: '',
    companyId: '',
    departmentId: '',
    employmentStatus: '',
  };
}

function buildListQuery(filters: PersonListFilters) {
  return {
    keyword: filters.keyword,
    companyId: filters.companyId,
    departmentId: filters.departmentId,
    employmentStatus: filters.employmentStatus,
  };
}

function PersonListFiltersPanel({
  filters,
  setFilters,
}: StandardCrudListExtensionContext<PersonListFilters, PersonListRow>) {
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void getCompanies()
      .then((items) => {
        if (!cancelled) {
          setCompanies(items);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setError('公司选项加载失败，当前仅保留关键字和状态筛选。');
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    if (!filters.companyId) {
      setDepartments([]);
      return () => {
        cancelled = true;
      };
    }

    void getDepartments(filters.companyId)
      .then((items) => {
        if (!cancelled) {
          setDepartments(items);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setDepartments([]);
          setError('部门选项加载失败，当前仅保留关键字、公司和状态筛选。');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [filters.companyId]);

  const companyOptions = useMemo(() => companies.map((company) => ({ value: company.id, label: company.name })), [companies]);
  const departmentOptions = useMemo(() => departments.map((department) => ({ value: department.id, label: department.name })), [departments]);

  return (
    <div style={{ display: 'grid', gap: 10 }}>
      {error ? <ErrorState message={error} /> : null}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10 }}>
        <Input
          label="关键字"
          value={filters.keyword}
          onChange={(event) => setFilters((current) => ({ ...current, keyword: event.target.value }))}
          placeholder="按工号 / 姓名搜索"
        />
        <Select
          label="公司"
          value={filters.companyId}
          onChange={(event) => setFilters((current) => ({ ...current, companyId: event.target.value, departmentId: '' }))}
          options={companyOptions}
          placeholder="全部公司"
        />
        <Select
          label="部门"
          value={filters.departmentId}
          onChange={(event) => setFilters((current) => ({ ...current, departmentId: event.target.value }))}
          options={departmentOptions}
          placeholder={filters.companyId ? '全部部门' : '请先选择公司'}
          disabled={!filters.companyId}
        />
        <Select
          label="在职状态"
          value={filters.employmentStatus}
          onChange={(event) => setFilters((current) => ({ ...current, employmentStatus: event.target.value }))}
          options={employmentStatusOptions}
          placeholder="全部状态"
        />
      </div>
    </div>
  );
}

function PersonRowActions({ row, refresh }: { row: PersonListRow; refresh: () => void }) {
  const [disabling, setDisabling] = useState(false);

  async function handleDisable() {
    if (!row.isEnabled) {
      return;
    }

    if (!window.confirm(`确认停用人员“${row.name}”吗？`)) {
      return;
    }

    try {
      setDisabling(true);
      await disablePerson(row.id);
      refresh();
    } finally {
      setDisabling(false);
    }
  }

  return (
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      <Button size="sm" onClick={() => window.location.assign(`/organization/persons/${row.id}`)}>查看</Button>
      <Button size="sm" onClick={() => window.location.assign(`/organization/persons/${row.id}`)}>编辑</Button>
      <Button size="sm" variant="danger" disabled={!row.isEnabled || disabling} onClick={() => void handleDisable()}>
        {disabling ? '停用中...' : '停用'}
      </Button>
    </div>
  );
}

function PersonDetailContent({ detail, refresh }: { detail: PersonDetailView; refresh: () => void }) {
  async function handleSave(payload: PersonUpsertRequest) {
    await updatePerson(detail.id, payload);
    refresh();
  }

  return (
    <div className="sh-page-stack">
      <PersonEditorForm
        person={detail}
        onSave={handleSave}
        sections={[
          { key: 'basic', titleKey: '基础信息', fields: ['PersonNo', 'ENAME'] },
          { key: 'organization', titleKey: '组织信息', fields: ['CompanyId', 'DepartmentId', 'PositionId', 'EmploymentStatus'] },
        ]}
        resolveTitle={(titleKey, fallback) => titleKey || fallback}
      />
      <PersonUserLinkCard summary={detail.userLinkSummary} title="账号绑定" />
      <AuthorizationSummaryCard summary={detail.authorizationSummary} title="授权摘要" />
    </div>
  );
}

export const personStandardCrudResource: StandardCrudResourceRegistration<PersonListFilters, PersonListRow, PersonDetailView> = {
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
  listViewKey: 'organization.person.list',
  detailViewKey: 'organization.person.detail',
  detailRoutePath: '/organization/persons/:id',
  permissions: ['Organization.Person.View'],
  createEmptyFilters,
  buildListQuery,
  mapListItem: (value) => value as PersonListRow,
  getRowId: (row) => row.id,
  buildColumns: () => [
    { key: 'personNo', title: '工号' },
    { key: 'name', title: '姓名' },
    { key: 'companyName', title: '公司', render: (item) => item.companyName || item.companyId },
    { key: 'departmentName', title: '部门', render: (item) => item.departmentName || item.departmentId },
    {
      key: 'employmentStatus',
      title: '在职状态',
      render: (item) => <StatusBadge variant="info" label={item.employmentStatusLabel || getEmploymentStatusLabel(item.employmentStatus)} />,
    },
    {
      key: 'isEnabled',
      title: '启用状态',
      render: (item) => <StatusBadge variant={item.isEnabled ? 'success' : 'warning'} label={item.isEnabled ? '启用' : '停用'} />,
    },
  ],
  renderFilters: (context) => <PersonListFiltersPanel {...context} />,
  renderHeaderActions: (context) => (
    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
      <Button size="sm" onClick={() => window.location.assign('/organization/persons/new')}>新建</Button>
      <Button size="sm" variant="primary" onClick={context.refresh} disabled={context.loading}>
        {context.loading ? '刷新中...' : '刷新'}
      </Button>
    </div>
  ),
  renderRowActions: (row, context) => <PersonRowActions row={row} refresh={context.refresh} />,
  mapDetail: (detail) => {
    const fields = detail.fields;
    return {
      id: String(fields.EID ?? ''),
      personNo: String(fields.PersonNo ?? ''),
      name: String(fields.ENAME ?? ''),
      companyId: String(fields.CompanyId ?? ''),
      departmentId: String(fields.DepartmentId ?? ''),
      positionId: typeof fields.PositionId === 'string' ? fields.PositionId : null,
      employmentStatus: String(fields.EmploymentStatus ?? 'FullTime'),
      employmentStatusLabel: getEmploymentStatusLabel(String(fields.EmploymentStatus ?? 'FullTime')),
      isEnabled: fields.IsEnabled !== false,
      createdAt: String(fields.CreatedAt ?? ''),
      companyName: typeof fields.CompanyName === 'string' ? fields.CompanyName : null,
      departmentName: typeof fields.DepartmentName === 'string' ? fields.DepartmentName : null,
      positionName: typeof fields.PositionName === 'string' ? fields.PositionName : null,
      userLinkSummary: detail.summaries.userLink as PersonDetailView['userLinkSummary'],
      authorizationSummary: detail.summaries.authorizationSummary as PersonDetailView['authorizationSummary'],
    };
  },
  renderDetail: ({ detail, refresh }) => <PersonDetailContent detail={detail} refresh={refresh} />,
  renderDetailHeaderActions: ({ detail, refresh }) => (
    detail.isEnabled
      ? <Button variant="danger" onClick={() => void disablePerson(detail.id).then(refresh)}>停用</Button>
      : <Button disabled>已停用</Button>
  ),
};
```

- [ ] **Step 5: 注册人员标准 CRUD 资源**

Modify `frontend/modules/organization/src/index.ts` by adding the import and property below. Do not change the existing `customPages`, `routes`, `menus`, or `permissions` arrays.

```ts
import { personStandardCrudResource } from './standard-crud/personStandardCrudResource';
```

Insert this property in `organizationModule` immediately after `metadata`:

```ts
  standardCrudResources: [
    personStandardCrudResource,
  ],
```

Remove the existing `standardListViews` property from `organizationModule` so the runtime list entry is owned by `standardCrudResources`.

- [ ] **Step 6: 收缩人员列表兼容入口**

Modify `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`:

```tsx
import type { StandardListViewRendererProps } from '@icp-platform/sdk';

export default function PersonListStandardListEntry(_props: StandardListViewRendererProps) {
  return (
    <div className="sh-error-state">
      人员中心标准列表入口已迁移到 standardCrudResources，请通过 runtime metadata 入口访问。
    </div>
  );
}
```

This file remains only for old direct imports during the migration window. Do not import Shell internals from Organization.

- [ ] **Step 7: 改造人员列表和详情页面包装**

Modify `frontend/modules/organization/src/pages/PersonListPage.tsx`:

```tsx
import { ErrorState } from '@icp-platform/ui';

export default function PersonListPage() {
  return (
    <ErrorState message="人员中心主列表已由 Shell 标准 CRUD 宿主接管，请通过 /organization/persons 的 metadata 入口访问。" />
  );
}
```

Modify `frontend/modules/organization/src/pages/PersonDetailPage.tsx`:

```tsx
import { ErrorState } from '@icp-platform/ui';

export default function PersonDetailPage() {
  return (
    <ErrorState message="人员详情已由 Shell 标准 CRUD 详情路由接管，请通过 /organization/persons/:id 访问。" />
  );
}
```

- [ ] **Step 8: Run Organization build**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected: PASS. The final Organization code must not import from `@icp-platform/shell` or `frontend/packages/shell/src/**`.

- [ ] **Step 9: Run person center Shell tests**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- organization-person-center-list-layout.test.tsx organization-person-center-detail-layout.test.tsx organization-person-create-flow.test.tsx standard-list-page-host.test.tsx
```

Expected: PASS. Tests should assert user-visible list/detail behavior and standard CRUD registration, not the internals of `PersonListStandardListEntry`.

- [ ] **Step 10: 扫描第一批残留**

Run:

```powershell
rg -n "StandardCrudListResult|StandardCrudDetail|normalizeSummaryBlock|normalizePersonListResponse|normalizePersonDetail|getPersonListRuntimeSchema|getPersonDetailRuntimeSchema" frontend/modules/organization/src frontend/packages
```

Expected:

- `StandardCrudListResult` / `StandardCrudDetail` should be imported from `@icp-platform/sdk`, not declared in `organizationService.ts`.
- `getPersonListRuntimeSchema` should no longer drive the main list entry.
- `getPersonDetailRuntimeSchema` may remain only for `PersonCreatePage` until second batch.
- Any hit in Organization must be explainable as second-batch residue.

- [ ] **Step 11: Commit**

```powershell
git add .\frontend\modules\organization\src .\frontend\packages\shell\src\__tests__
git commit -m "重构：人员中心接入前端标准CRUD宿主"
```

---

### Task 6: 验证记录与第二批遗留登记

**Files:**
- Create: `docs/verification/frontend-standard-crud-platformization-phase-1-verification.md`
- Modify: `docs/phases/current-handoff.md`

- [ ] **Step 1: 创建第一批验证记录**

Create `docs/verification/frontend-standard-crud-platformization-phase-1-verification.md`:

```md
# 前端标准 CRUD 平台化第一批验证

## 1. 范围

本次验证覆盖前端标准 CRUD 第一批平台化：

- SDK 标准 CRUD 前端契约与客户端 helper。
- Shell 标准 CRUD schema resolver、registry、列表 Host、详情 Host。
- Organization 人员中心试点接入。
- 第二批遗留登记。

本次不覆盖：

- 通用字段渲染器。
- 通用 lookup 数据源。
- 通用动作扩展与权限过滤。
- 通用 summary block 渲染协议。
- 通用 i18n key 解析。

## 2. 已执行验证

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "StandardCrudListResult|StandardCrudDetail|normalizeSummaryBlock|normalizePersonListResponse|normalizePersonDetail|getPersonListRuntimeSchema|getPersonDetailRuntimeSchema" frontend/modules/organization/src frontend/packages
```

## 3. 验证结论

- 标准 CRUD 类型与 helper 已由 `@icp-platform/sdk` 提供。
- Shell 标准 CRUD Host 已接管人员中心列表/详情主流程。
- Organization 不再持有完整标准 CRUD 列表状态机。
- 人员账号绑定、授权摘要、组织归属 lookup 和人员编辑表单仍留在 Organization，作为第二批输入。

## 4. 第二批固定交付清单

第二批入口：`docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md` 第 14.2 节。

第二批必须覆盖：

- 通用字段渲染器：文本、布尔、枚举、引用、日期、只读展示。
- 通用编辑表单：create / edit / view、sections、必填、只读、默认值、提交值规范化。
- 通用数据源 lookup：enum、dictionary、value list、model lookup、显式级联依赖。
- 通用动作扩展：标准动作、模块动作、确认文案、pending 状态、错误提示、权限过滤。
- 通用 summary block 渲染协议：平台处理状态，模块只提供专属内容 renderer。
- 通用 i18n key 解析：统一 `dicKey + localKey` 规则。
- Organization 人员中心继续收缩，只保留人员账号绑定、授权摘要、组织归属特殊显示和必要业务校验。

## 5. 第一批暂留项与原因

- `PersonEditorForm`：表单字段渲染和级联 lookup 属于第二批。
- `getCompanies / getDepartments / getPositions`：组织归属 lookup 仍由 Organization 承接，第二批再通过 lookup provider 平台化。
- `PersonUserLinkCard`：人员账号绑定是 Organization 专属摘要展示。
- `AuthorizationSummaryCard`：授权摘要来源和业务含义仍由模块声明，第二批只抽 summary block 状态协议。
- `PersonCreatePage`：新建表单仍依赖 `PersonEditorForm`，第二批再收缩。

## 6. 已知风险

- 第一批只移走列表/详情主状态机，表单和 lookup 仍可能在下一批前出现重复模式。
- 如果第二批不推进，Organization 仍会保留较多字段渲染和选项加载代码。
- 如果后续模块直接复制 `PersonEditorForm`，会绕开本设计的第二批目标。

## 7. 回滚思路

- 回滚 SDK 标准 CRUD helper 与 Shell Host 相关提交。
- 恢复 Organization 原 `PersonListStandardListEntry` 和 `PersonDetailPage` 状态机。
- 保留本验证记录或在回滚提交中追加说明，避免第二批状态误判为已完成。
```

- [ ] **Step 2: 更新当前交接台账**

Modify `docs/phases/current-handoff.md`:

```md
### 前端标准 CRUD 平台化第一批

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

- 按设计第 14.2 节继续第二批：字段渲染器、lookup、动作扩展、summary block、权限动作过滤和 i18n key 解析。
```

Place this under the current "最近完成" or "当前推荐下一步" section without deleting unrelated existing entries.

- [ ] **Step 3: 运行完整前端验证**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected: all PASS.

- [ ] **Step 4: 运行残留扫描**

Run:

```powershell
rg -n "StandardCrudListResult|StandardCrudDetail|normalizeSummaryBlock|normalizePersonListResponse|normalizePersonDetail|getPersonListRuntimeSchema|getPersonDetailRuntimeSchema" frontend/modules/organization/src frontend/packages
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|PersonUserLinkCard|AuthorizationSummaryCard" frontend/modules/organization/src
```

Expected:

- 第一条扫描中的通用标准 CRUD 类型和 helper 不应在 Organization 私有代码中重新定义。
- 第二条扫描允许命中，且必须在验证记录第 5 节解释为第二批暂留项。

- [ ] **Step 5: Commit**

```powershell
git add .\docs\verification\frontend-standard-crud-platformization-phase-1-verification.md .\docs\phases\current-handoff.md
git commit -m "验证：记录前端标准CRUD第一批平台化"
```

---

## 最终验证

- [ ] **Step 1: 运行设计目标校验**

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-12-frontend-standard-crud-platformization-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

Expected: PASS。

- [ ] **Step 2: 运行全量前端验证**

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected: PASS。

- [ ] **Step 3: 确认第二批登记存在**

```powershell
Select-String -Path .\docs\verification\frontend-standard-crud-platformization-phase-1-verification.md -Pattern "第二批固定交付清单|PersonEditorForm|getCompanies|getDepartments|getPositions|summary block"
Select-String -Path .\docs\phases\current-handoff.md -Pattern "前端标准 CRUD 平台化|字段渲染器|lookup|summary block"
```

Expected: 两个文件都能定位到第二批入口和暂留项。

---

## Self-Review

**Spec coverage:**  
Task 1 覆盖 SDK 标准 CRUD 契约和客户端 helper；Task 2 覆盖 schema resolver 与 registry；Task 3 覆盖标准列表 Host；Task 4 覆盖标准详情 Host；Task 5 覆盖 Organization 试点接入；Task 6 覆盖验证记录、handoff 和第二批防遗忘机制。

**Placeholder scan:**  
本计划已扫描常见占位词、模糊实施语和空泛测试要求。执行者如果新增或发现占位内容，必须先修正计划再执行。

**Type consistency:**  
统一使用 `StandardCrudResourceRegistration`、`StandardCrudListExtensionContext`、`StandardCrudDetailExtensionContext`、`StandardCrudListResult`、`StandardCrudDetail`、`StandardCrudSummaryBlock`。资源键统一为 `organization.person`，列表 viewKey 统一为 `organization.person.list`，详情 viewKey 统一为 `organization.person.detail`。
