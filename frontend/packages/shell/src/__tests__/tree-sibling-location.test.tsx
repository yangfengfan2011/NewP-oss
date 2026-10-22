// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type PageProps } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => { await Promise.all([import('../composition/StandardTreePage'), import('../composition/StandardTabPage')]); });
it('keeps sibling tree instances separate and restores the active instance from a shared address', async () => {
  const nodes = ['a', 'b'].map(nodeKey => ({ nodeKey, nodeType: 'n', label: nodeKey, hasChildren: false }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'Test', version: '1' }, treeProviders: [{ key: 'tree', roots: async () => nodes, children: async () => [], path: async (_scope, key) => nodes.filter(node => node.nodeKey === key), search: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }), resolveChange: async () => ({ kind: 'unrelated' }) }], pageInputResolvers: [{ key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }], pages: [{ pageKey: 'content', load: async () => ({ default: ({ input }: PageProps) => <label>{input.context?.nodeKey}:<input aria-label="local query" /></label> }) }] }], 'test');
  const tree = { key: 'tree', mode: 'tree', dicKey: 'Test', providerKey: 'tree', nodeTypes: [{ key: 'n', titleKey: 'Node', pageRef: { pageKey: 'platform.tabs', parameters: { viewKey: 'inner' } } }] };
  const inner = { key: 'inner', mode: 'tab', dicKey: 'Test', defaultTabKey: 'basic', tabs: ['basic', 'children'].map(key => ({ key, titleKey: key, pageRef: { pageKey: 'content' } })) };
  const outer = { key: 'outer', mode: 'tab', dicKey: 'Test', defaultTabKey: 'A', tabs: ['A', 'B'].map(key => ({ key, titleKey: key, pageRef: { pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' } })) };
  const mount = async (address: string) => {
    const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
    const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [tree, inner, outer].map(view => ({ type: 'view', view })) }}><PageEntry page={{ pageKey: 'platform.tabs', parameters: { viewKey: 'outer' } }} /></RuntimeMetadataProvider></LocalizationProvider> }], { initialEntries: [address] });
    await act(async () => root.render(<RouterProvider router={router} />));
    const panel = () => {
      const activeTab = container.querySelector('[role=tablist] [role=tab][aria-selected=true]');
      return document.getElementById(activeTab?.getAttribute('aria-controls') ?? '')!;
    };
    const wait = async () => { for (let i = 0; i < 100 && !panel()?.querySelector('input[aria-label="local query"]'); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); }); expect(panel()?.querySelector('input[aria-label="local query"]')).not.toBeNull(); };
    await wait();
    return { container, router, panel, wait, dispose: async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); } };
  };
  const mounted = await mount('/?tab=A&node=a&nodeTab=children&other=keep');
  let address = '';
  try {
    mounted.panel().querySelector<HTMLInputElement>('input[aria-label="local query"]')!.value = 'keep A';
    await act(async () => [...mounted.container.querySelectorAll<HTMLButtonElement>('[role=tab]')].find(tab => tab.textContent === 'B')!.click()); await mounted.wait();
    await act(async () => [...mounted.panel().querySelectorAll<HTMLElement>('.ant-tree-title')].find(title => title.textContent === 'b')!.click());
    await act(async () => [...mounted.panel().querySelectorAll<HTMLButtonElement>('[role=tab]')].find(tab => tab.textContent === 'children')!.click());
    await act(async () => [...mounted.container.querySelectorAll<HTMLButtonElement>('[role=tab]')].find(tab => tab.textContent === 'A')!.click());
    expect(mounted.panel().querySelector('.ant-tree-node-selected')?.textContent).toBe('a');
    expect(mounted.panel().querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('children');
    expect(mounted.panel().querySelector<HTMLInputElement>('input[aria-label="local query"]')?.value).toBe('keep A');
    expect(new URLSearchParams(mounted.router.state.location.search).get('node')).toBe('a');
    await act(async () => mounted.router.navigate(-1)); await mounted.wait();
    expect(mounted.container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('B');
    expect(mounted.panel().querySelector('.ant-tree-node-selected')?.textContent).toBe('b');
    expect(mounted.panel().querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('children');
    await act(async () => mounted.router.navigate(1)); await mounted.wait();
    expect(mounted.container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('A');
    expect(mounted.panel().querySelector('.ant-tree-node-selected')?.textContent).toBe('a');
    expect(mounted.panel().querySelector<HTMLInputElement>('input[aria-label="local query"]')?.value).toBe('keep A');
    expect(new URLSearchParams(mounted.router.state.location.search).get('other')).toBe('keep');
    address = '/' + mounted.router.state.location.search;
  } finally { await mounted.dispose(); }
  const refreshed = await mount(address);
  try { expect(refreshed.panel().querySelector('.ant-tree-node-selected')?.textContent).toBe('a'); expect(refreshed.panel().querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('children'); }
  finally { await refreshed.dispose(); }
});
