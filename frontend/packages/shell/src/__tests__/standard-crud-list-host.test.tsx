// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
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
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{
            zhs: {
              'PersonListView.Title': '人员中心',
              'PersonListView.Description': '维护组织模块中的人员基础信息。',
            },
          }}
        >
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
          </RuntimeMetadataProvider>
        </LocalizationProvider>,
      );
    });
  }

  function mockStandardCrudModule(
    overrides: Partial<NonNullable<ReturnType<typeof ModuleRegistry.getAllModules>[number]['standardCrudResources']>[number]> = {},
  ) {
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
            ...overrides,
          },
        ],
      },
    ]);
  }

  it('loads rows through standard CRUD registration', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [{ id: 'Person_1', name: '张三' }], total: 1, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await act(async () => undefined);

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons?pageIndex=1&pageSize=20', { method: 'GET' });
    expect(container.textContent).toContain('张三');
    expect(container.textContent).toContain('共 1 条');
  });

  it('resolves view title and description from the runtime dictionary', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await act(async () => undefined);

    expect(container.textContent).toContain('人员中心');
    expect(container.textContent).toContain('维护组织模块中的人员基础信息。');
    expect(container.textContent).not.toContain('Title');
    expect(container.textContent).not.toContain('Description');
  });

  it('renders custom header actions only in the page header', async () => {
    mockStandardCrudModule({
      renderHeaderActions: () => <button type="button">自定义操作</button>,
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [{ id: 'Person_1', name: '张三' }], total: 1, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await act(async () => undefined);

    expect(container.textContent?.match(/自定义操作/g)).toHaveLength(1);
  });

  it('renders error state without an empty state after load failure', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('列表接口不可用'));

    await renderHost();
    await act(async () => undefined);

    expect(container.textContent).toContain('列表接口不可用');
    expect(container.textContent).not.toContain('暂无符合筛选条件的数据。');
  });

  it('uses normalized paging metadata in the footer', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [{ id: 'Person_21', name: '李四' }], total: 41, page: 2, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await act(async () => undefined);

    expect(container.textContent).toContain('共 41 条');
    expect(container.textContent).toContain('第 2 / 3 页');
  });

  it('renders a clear error when registration is missing', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([]);

    await renderHost();

    expect(container.textContent).toContain('未找到标准 CRUD 前端资源注册项');
  });
});
