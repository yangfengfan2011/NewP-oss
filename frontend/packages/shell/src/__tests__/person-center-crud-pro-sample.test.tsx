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

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T | PromiseLike<T>) => void;
  reject: (reason?: unknown) => void;
}

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
  return Array.from(container.querySelectorAll<HTMLElement>('button, a, [role="button"]')).find(
    (button) => button.textContent?.replace(/\s+/g, '').includes(text.replace(/\s+/g, '')),
  );
}

function findAntdButtonsByText(container: ParentNode, text: string): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>('button, a, [role="button"]')).filter(
    (button) => button.textContent?.replace(/\s+/g, '').includes(text.replace(/\s+/g, '')),
  );
}

function findTableRowByText(container: ParentNode, text: string): HTMLTableRowElement | null {
  return Array.from(container.querySelectorAll('tbody tr')).find((row) => row.textContent?.includes(text)) ?? null;
}

function findRowActionButton(container: ParentNode, rowText: string, buttonText: string): HTMLElement | undefined {
  const row = findTableRowByText(container, rowText);
  if (!row) {
    return undefined;
  }

  return findAntdButtonByText(row, buttonText);
}

async function clickPopconfirmButton(label: '确认' | '取消') {
  const button = Array.from(document.body.querySelectorAll<HTMLElement>('button')).find(
    (item) => item.textContent?.replace(/\s+/g, '') === label,
  );

  expect(button).toBeDefined();

  await act(async () => {
    button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
}

function findInputByLabel(container: ParentNode, labelText: string): HTMLInputElement | null {
  const label = Array.from(container.querySelectorAll('label')).find((candidate) => candidate.textContent?.includes(labelText));
  if (!label) {
    return null;
  }

  const fieldId = label.getAttribute('for');
  if (!fieldId) {
    return null;
  }

  const ownerDocument = container instanceof Document ? container : container.ownerDocument;
  const field = ownerDocument?.getElementById(fieldId);
  return field instanceof HTMLInputElement ? field : null;
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: Deferred<T>['resolve'];
  let reject!: Deferred<T>['reject'];
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve;
    reject = innerReject;
  });

  return { promise, resolve, reject };
}

function setReactInputValue(input: HTMLInputElement, value: string) {
  const descriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
  descriptor?.set?.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
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

  it('乱序详情响应不会回写当前编辑抽屉的数据', async () => {
    const person1Detail = createDeferred<Response>();
    const person2Detail = createDeferred<Response>();

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        return person1Detail.promise;
      }
      if (url.includes('/person-2')) {
        return person2Detail.promise;
      }

      return new Response(JSON.stringify({
        items: [
          { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
          { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: true },
        ],
        total: 2,
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

    const editButtons = findAntdButtonsByText(container, '编辑');
    expect(editButtons).toHaveLength(2);

    await act(async () => {
      editButtons[0]?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      editButtons[1]?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    person2Detail.resolve(new Response(JSON.stringify({
      fields: {
        EID: 'person-2',
        PersonNo: 'P0002',
        ENAME: '李四',
        IsEnabled: true,
      },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    await flushAsync();

    expect(findInputByLabel(document.body, '姓名')?.value).toBe('李四');

    person1Detail.resolve(new Response(JSON.stringify({
      fields: {
        EID: 'person-1',
        PersonNo: 'P0001',
        ENAME: '张三',
        IsEnabled: true,
      },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('编辑人员');
    expect(findInputByLabel(document.body, '姓名')?.value).toBe('李四');
  });

  it('编辑详情加载失败时保留详情抽屉错误而不是打开空编辑表单', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        throw new Error('详情加载失败，请稍后重试');
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

    const editButton = findAntdButtonByText(container, '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('人员中心');
    expect(document.body.textContent).toContain('详情加载失败，请稍后重试');
    expect(document.body.textContent).not.toContain('保存修改');
    expect(findInputByLabel(document.body, '姓名')).toBeNull();
  });

  it('新建抽屉中的工号字段可编辑', async () => {
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

    const createButton = findAntdButtonByText(container, '新建人员');
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    const personNoInput = findInputByLabel(document.body, '工号');
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('新建人员');
    expect(personNoInput).not.toBeNull();
    expect(personNoInput?.readOnly).toBe(false);
  });

  it('人员中心声明删除时按危险确认动作渲染', async () => {
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

    const deleteButton = findAntdButtonByText(container, '删除');
    expect(deleteButton).toBeDefined();

    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.textContent).toContain('确认删除该人员吗？');
  });

  it('资源未声明删除时不展示删除入口', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const sampleModule = {
      ...organizationModule,
      standardCrudResources: [{
        ...resource,
        listActions: resource.listActions?.filter((action) => action.standardAction !== 'delete'),
        actionHandlers: Object.fromEntries(
          Object.entries(resource.actionHandlers ?? {}).filter(([key]) => key !== 'delete'),
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

    expect(container.textContent).not.toContain('删除');
    expect(container.textContent).toContain('停用');
  });

  it('delete handler 即使不主动 refresh，确认删除成功后 Host 也会刷新一次列表', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const deleteHandler = vi.fn(async () => {});
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
    let listRequestCount = 0;
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      listRequestCount += 1;
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

    const deleteButton = findRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();

    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    await clickPopconfirmButton('确认');
    await flushAsync();

    expect(deleteHandler).toHaveBeenCalledTimes(1);
    expect(listRequestCount).toBe(2);
  });

  it('删除失败只在对应行展示错误，不刷新列表，且保留当前行详情抽屉', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const deleteHandler = vi.fn(async () => {
      throw new Error('删除张三失败');
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
    let listRequestCount = 0;
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
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

      listRequestCount += 1;
      return new Response(JSON.stringify({
        items: [
          { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
          { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: true },
        ],
        total: 2,
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

    const viewButton = findRowActionButton(container, '张三', '查看');
    expect(viewButton).toBeDefined();
    await act(async () => viewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('人员中心');

    const deleteButton = findRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();
    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    await clickPopconfirmButton('确认');
    await flushAsync();

    const firstRow = findTableRowByText(container, '张三');
    const secondRow = findTableRowByText(container, '李四');
    expect(firstRow?.textContent).toContain('删除张三失败');
    expect(secondRow?.textContent).not.toContain('删除张三失败');
    expect(Array.from(container.querySelectorAll('[role="alert"]')).filter((item) => item.textContent?.includes('删除张三失败'))).toHaveLength(1);
    expect(listRequestCount).toBe(1);
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('人员中心');
    expect(document.body.textContent).toContain('详情信息');
  });

  it('删除等待期间打开其他记录详情后，成功回调不会误关新抽屉', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const deferredDelete = createDeferred<void>();
    const deleteHandler = vi.fn(() => deferredDelete.promise);
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

      if (url.includes('/person-2')) {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-2',
            PersonNo: 'P0002',
            ENAME: '李四',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      return new Response(JSON.stringify({
        items: [
          { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
          { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: true },
        ],
        total: 2,
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

    const firstViewButton = findRowActionButton(container, '张三', '查看');
    expect(firstViewButton).toBeDefined();
    await act(async () => firstViewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('张三');

    const deleteButton = findRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();
    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    await clickPopconfirmButton('确认');
    await flushAsync();

    const secondViewButton = findRowActionButton(container, '李四', '查看');
    expect(secondViewButton).toBeDefined();
    await act(async () => secondViewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('李四');

    deferredDelete.resolve();
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();
    expect(document.body.textContent).toContain('李四');
  });

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

    const keyword = container.querySelector('input[placeholder="按工号 / 姓名搜索"]');
    expect(keyword).toBeInstanceOf(HTMLInputElement);

    await act(async () => {
      setReactInputValue(keyword as HTMLInputElement, 'P0001');
    });

    const searchButton = findAntdButtonByText(document.body, '搜索');
    expect(searchButton).toBeDefined();
    await act(async () => searchButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    const refreshButton = findAntdButtonByText(document.body, '刷新');
    expect(refreshButton).toBeDefined();
    await act(async () => refreshButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(String(fetcher.mock.calls.at(-1)?.[0] ?? '')).toContain('keyword=P0001');
  });
});
