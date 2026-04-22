// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { ModuleRegistry } from '@icp-platform/sdk';
import StandardListPageHost from '../pages/StandardListPageHost';
import { buildCustomPageRegistry } from '../registries/customPageRegistry';
import { buildListViewRegistry } from '../registries/listViewRegistry';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const standardListPageMock = vi.hoisted(() =>
  vi.fn(({ title }: { title: string }) => <div data-testid="standard-list-page">{title}</div>),
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
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            viewDocuments,
            modelDocuments: [],
          }}
        >
          <StandardListPageHost viewKey="organization.person.list" />
        </RuntimeMetadataProvider>,
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
