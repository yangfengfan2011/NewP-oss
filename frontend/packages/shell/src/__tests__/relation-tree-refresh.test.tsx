// @vitest-environment jsdom
import { act, useContext } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import { CompositionContext } from '../composition/pageCompositionRuntime';
import organizationTree from '../../../../modules/platform/organization/metadata/views/OrganizationStructure.view.json';

const { fetchMock } = vi.hoisted(() => ({ fetchMock: vi.fn() }));
vi.mock('@icp-platform/core', async importOriginal => ({ ...await importOriginal<object>(), platformFetch: fetchMock }));
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

it.each(['rename', 'exclude', 'retry', 'unknown', 'unknown-retry'])('automatically refreshes a configured filtered tree after %s and retries only reads', async scenario => {
  let saved = false;
  let failed = false;
  let handoffError: unknown;
  const company = { nodeKey: 'company:c', nodeType: 'company', label: 'Company', hasChildren: true };
  const department = () => ({ nodeKey: 'department:d', nodeType: 'department', label: saved ? 'Renamed' : 'Department', hasChildren: false });
  fetchMock.mockReset();
  fetchMock.mockImplementation(async (address: string) => {
    const url = new URL(address, 'http://localhost');
    if (saved && scenario.includes('retry') && !failed) { failed = true; return { ok: false, status: 500 }; }
    if (url.pathname.endsWith('/path') && url.searchParams.get('nodeKey') === 'department:d') {
      return saved && scenario === 'exclude' ? { ok: false, status: 404 } : { ok: true, json: async () => [company, department()] };
    }
    const result = url.pathname.endsWith('/children') ? (saved && scenario === 'exclude' ? [] : [department()]) : [company];
    return { ok: true, json: async () => result };
  });
  const write = vi.fn(async () => { saved = true; });
  function Content() {
    const runtime = useContext(CompositionContext)!;
    return <button onClick={async () => {
      await write();
      try { await runtime.committed({ resourceKey: 'organization.department', operation: 'update', id: 'd',
        ...(scenario.startsWith('unknown') ? {} : {
          before: { CompanyId: 'c', ParentDepartmentId: null, IsEnabled: true },
          after: { CompanyId: 'c', ParentDepartmentId: null, IsEnabled: scenario !== 'exclude' },
        }) }); }
      catch (error) { handoffError = error; }
    }}>Save</button>;
  }
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'Test', version: '1' },
    pageInputResolvers: [{ key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: { kind: 'global' } }) }],
    pages: [{ pageKey: 'content', load: async () => ({ default: Content }) }],
  }], 'test');
  const view = { ...structuredClone(organizationTree.view), treeActions: [], nodeTypes: ['company', 'department'].map(key => ({ key, titleKey: key, pageRef: { pageKey: 'content' } })) };
  Object.assign(view.relationSource.nodes[1], { filters: [{ field: 'IsEnabled', operator: '=', value: true }] });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [{ type: 'view', view }] }}><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: view.key }, inputResolverKey: 'scope' }} /></RuntimeMetadataProvider></LocalizationProvider> }], { initialEntries: ['/?node=department:d'] });
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    for (let i = 0; i < 100 && ![...container.querySelectorAll('button')].some(button => button.textContent === 'Save'); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('Department');
    expect(container.querySelector('.np-tree-context-header .ant-breadcrumb')?.textContent).toBe('Company/Department');
    fetchMock.mockClear();
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === 'Save')!.click());
    if (scenario.includes('retry')) {
      expect(container.textContent).toContain('已保存，刷新失败');
      expect(container.textContent).not.toContain('树读取失败 (500)');
      await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '重试读取')!.click());
    } else expect(handoffError).toBeUndefined();
    expect(container.textContent).not.toContain('已保存，刷新失败');
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/roots?'))).toBe(true);
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/children?'))).toBe(true);
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe(scenario === 'exclude' ? 'Company' : 'Renamed');
    expect(container.querySelector('.np-tree-context-header .ant-breadcrumb')?.textContent).toBe(scenario === 'exclude' ? 'Company' : 'Company/Renamed');
    if (scenario === 'exclude') expect(container.querySelector('aside')?.textContent).not.toContain('Department');
    expect(write).toHaveBeenCalledTimes(1);
    if (scenario !== 'exclude') {
      const reads = fetchMock.mock.calls.length;
      await act(async () => container.querySelector<HTMLButtonElement>('.ant-breadcrumb button')!.click());
      expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('Company');
      expect(new URLSearchParams(router.state.location.search).get('node')).toBe('company:c');
      expect(fetchMock).toHaveBeenCalledTimes(reads);
    }
  } finally { await act(async () => root.unmount()); router.dispose(); container.remove(); }
});
