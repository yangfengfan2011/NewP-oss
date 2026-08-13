// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { ModuleRegistry } from '@icp-platform/sdk';
import StandardListPageHost from '../pages/StandardListPageHost';
import { buildCustomPageRegistry } from '../registries/customPageRegistry';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const standardListPageMock = vi.hoisted(() =>
  vi.fn(({ title, content }: { title: string; content?: ReactNode }) => (
    <div data-testid="standard-list-page">
      {title}
      {content}
    </div>
  )),
);

vi.mock('@icp-platform/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@icp-platform/ui')>();

  return {
    ...actual,
    StandardListPage: standardListPageMock,
  };
});

describe('StandardListPageHost', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();
    standardListPageMock.mockClear();
    window.__ICP_RUNTIME_METADATA__ = undefined;

    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  function renderHost(viewDocuments: Array<{ type: 'view'; view: Record<string, unknown> }>) {
    container = document.createElement('div');
    document.body.appendChild(container);

    return act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter>
          <LocalizationProvider
            defaultLanguage="zhs"
            supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
            catalogs={{ zhs: {} }}
          >
            <RuntimeMetadataProvider
              value={{
              manifest: { modules: [] },
              navDocuments: [],
              viewDocuments,
              modelDocuments: [{ type: 'model', Person: { fields: [{ name: 'name' }] } }],
              }}
            >
              <StandardListPageHost viewKey="organization.person.list" />
            </RuntimeMetadataProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
  }

  it('renders an explicit error block when view metadata is missing', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: {
          moduleId: 'organization',
          displayName: 'Organization',
          version: '1.0.0',
        },
        standardListViews: [
          {
            viewKey: 'organization.person.list',
            component: () => null,
          },
        ],
      },
    ]);

    await renderHost([]);

    expect(container.textContent).toContain('未找到列表视图元数据');
  });

  it('renders an explicit error block when list page registration is missing', async () => {
    await renderHost([
      {
        type: 'view',
        view: {
          key: 'organization.person.list',
          type: 'list',
          title: '人员中心',
        },
      },
    ]);

    expect(container.textContent).toContain('未找到列表页注册项');
    expect(standardListPageMock).not.toHaveBeenCalled();
  });

  it('renders the list page through the registration callback', async () => {
    const registrationComponent = vi.fn(({ children }) => children({
      title: '人员中心',
      content: <div>列表内容</div>,
    }));

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: {
          moduleId: 'organization',
          displayName: 'Organization',
          version: '1.0.0',
        },
        standardListViews: [
          {
            viewKey: 'organization.person.list',
            component: registrationComponent,
          },
        ],
      },
    ]);

    await renderHost([
      {
        type: 'view',
        view: {
          key: 'organization.person.list',
          type: 'list',
          title: '人员中心元数据',
        },
      },
    ]);

    expect(registrationComponent).toHaveBeenCalledTimes(1);
    expect(standardListPageMock).toHaveBeenCalledTimes(1);
    expect(standardListPageMock.mock.calls[0]?.[0]).toMatchObject({
      title: '人员中心',
    });
    expect(container.textContent).toContain('人员中心');
  });

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
          model: 'Person',
          columns: [{ key: 'name', labelKey: 'Name', source: 'model', field: 'name' }],
        },
      },
    ]);
    await act(async () => undefined);

    expect(container.textContent).toContain('张三');
    expect(container.textContent).not.toContain('旧列表');
  });

  it('rejects list metadata when mode is detail', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: {
          moduleId: 'organization',
          displayName: 'Organization',
          version: '1.0.0',
        },
        standardListViews: [
          {
            viewKey: 'organization.person.list',
            component: standardListPageMock,
          },
        ],
      },
    ]);

    await renderHost([
      {
        type: 'view',
        view: {
          key: 'organization.person.list',
          mode: 'detail',
          title: '人员详情',
        },
      },
    ]);

    expect(container.textContent).toContain('未找到列表视图元数据');
  });

  it('drops duplicate list registrations and reports the conflict', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: {
          moduleId: 'organization-a',
          displayName: 'Organization A',
          version: '1.0.0',
        },
        standardListViews: [
          {
            viewKey: 'organization.person.list',
            component: standardListPageMock,
          },
        ],
      },
      {
        metadata: {
          moduleId: 'organization-b',
          displayName: 'Organization B',
          version: '1.0.0',
        },
        standardListViews: [
          {
            viewKey: 'organization.person.list',
            component: standardListPageMock,
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

    expect(errorSpy).toHaveBeenCalledTimes(1);
    expect(String(errorSpy.mock.calls[0]?.[0])).toContain('organization.person.list');
    expect(container.textContent).toContain('未找到列表页注册项');

    errorSpy.mockRestore();
  });

  it('drops duplicate custom page registrations and reports the conflict', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const registry = buildCustomPageRegistry([
      {
        metadata: {
          moduleId: 'module-a',
          displayName: 'Module A',
          version: '1.0.0',
        },
        customPages: [
          { pageKey: 'module.page', component: () => null },
        ],
      },
      {
        metadata: {
          moduleId: 'module-b',
          displayName: 'Module B',
          version: '1.0.0',
        },
        customPages: [
          { pageKey: 'module.page', component: () => null },
        ],
      },
    ]);

    expect(errorSpy).toHaveBeenCalledTimes(1);
    expect(String(errorSpy.mock.calls[0]?.[0])).toContain('module.page');
    expect(registry.has('module.page')).toBe(false);

    errorSpy.mockRestore();
  });
});
