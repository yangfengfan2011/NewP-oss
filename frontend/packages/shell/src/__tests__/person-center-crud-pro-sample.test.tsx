// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationModule from '../../../../modules/organization/src/index';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const organizationServiceMocks = vi.hoisted(() => ({
  getCompanies: vi.fn(async () => []),
  getDepartments: vi.fn(async () => []),
  getPositions: vi.fn(async () => []),
}));

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    getCompanies: organizationServiceMocks.getCompanies,
    getDepartments: organizationServiceMocks.getDepartments,
    getPositions: organizationServiceMocks.getPositions,
  };
});

async function flushAsync() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function findAntdButtonByText(container: ParentNode, text: string): HTMLElement | undefined {
  return Array.from(container.querySelectorAll<HTMLElement>('button.ant-btn, a.ant-btn')).find(
    (button) => button.textContent?.includes(text),
  );
}

function renderHost(container: HTMLElement, initialEntries = ['/organization/persons']) {
  const root = createRoot(container);
  root.render(
    <MemoryRouter initialEntries={initialEntries}>
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
      </LocalizationProvider>
    </MemoryRouter>,
  );
  return root;
}

describe('人员中心 CRUD Pro 样板', () => {
  let container: HTMLDivElement;
  let root: Root | null = null;

  beforeEach(() => {
    organizationServiceMocks.getCompanies.mockResolvedValue([]);
    organizationServiceMocks.getDepartments.mockResolvedValue([]);
    organizationServiceMocks.getPositions.mockResolvedValue([]);

    const getComputedStyle = window.getComputedStyle.bind(window);
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
    Object.defineProperty(window, 'getComputedStyle', {
      writable: true,
      value: (element: Element, pseudoElement?: string | null) => {
        if (pseudoElement) {
          return {
            getPropertyValue: () => '',
            width: '0px',
            height: '0px',
          };
        }

        return getComputedStyle(element);
      },
    });
  });

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

    const createButton = findAntdButtonByText(container, '新建人员');
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('新建人员');
    expect(document.body.textContent).toContain('新建人员');
    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();

    const closeButton = document.body.querySelector('.ant-drawer-close') as HTMLButtonElement;
    await act(async () => closeButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).not.toContain('编辑人员');
    expect(document.body.querySelector('.ant-drawer')).toBeNull();

    const viewButton = findAntdButtonByText(container, '查看');
    await act(async () => viewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('详情信息');
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('人员中心');

    const editButton = document.body.querySelector('.ant-drawer-extra button.ant-btn') as HTMLButtonElement | null;
    expect(editButton?.textContent?.replace(/\s+/g, '')).toContain('编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('编辑人员');
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('编辑人员');
  });

  it('平台新建按钮不依赖资源 create handler', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const sampleModule = {
      ...organizationModule,
      standardCrudResources: [{
        ...resource,
        actionHandlers: Object.fromEntries(
          Object.entries(resource.actionHandlers ?? {}).filter(([key]) => key !== 'create'),
        ),
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

    const createButton = findAntdButtonByText(container, '新建人员');
    expect(createButton).toBeDefined();

    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('新建人员');
  });
});
