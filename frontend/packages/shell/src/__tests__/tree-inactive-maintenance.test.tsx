// @vitest-environment jsdom
import { act, useContext } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type TreeProviderBinding } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import { CompositionContext } from '../composition/pageCompositionRuntime';
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
afterEach(() => { vi.restoreAllMocks(); });
const node = (nodeKey: string) => ({ nodeKey, nodeType: 'node', label: nodeKey, hasChildren: nodeKey !== 'd' });
it.each(['move', 'delete', 'retry'])('defers %s while inactive, preserves navigation and recovers selection through the Tree Host', async scenario => {
  let moved = false;
  let failed = false;
  const children = vi.fn(async (_scope, key) => {
    if (moved && scenario === 'retry' && !failed) { failed = true; throw new Error('offline'); }
    return moved && scenario === 'delete' ? [] : (key === (moved ? 'p2' : 'p1') ? [node('d')] : []);
  });
  const provider: TreeProviderBinding = { key: 'tree', roots: vi.fn(async () => [node('p1'), node('p2')]), children,
    path: vi.fn(async (_scope, key) => key === 'd' ? [node(moved ? 'p2' : 'p1'), node('d')] : [node(key)]),
    search: vi.fn(), resolveChange: async () => ({ kind: 'located', operation: scenario === 'delete' ? 'delete' : 'update', nodeKey: 'd', beforeLocation: { nodeKey: 'd', parentNodeKey: 'p1' }, afterLocation: scenario === 'delete' ? undefined : { nodeKey: 'd', parentNodeKey: 'p2' } }) };
  function Directory() { const runtime = useContext(CompositionContext)!; return <button onClick={async () => { moved = true; await runtime.committed({ resourceKey: 'resource', operation: 'update', id: 'd' }); }}>move</button>; }
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'Test', version: '1' }, treeProviders: [provider],
    pageInputResolvers: [{ key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }],
    pages: [{ pageKey: 'directory', load: async () => ({ default: Directory }) }, { pageKey: 'content', load: async () => ({ default: () => <p>content</p> }) }],
  }], 'test');
  const tree = { key: 'tree', mode: 'tree', dicKey: 'Test', providerKey: 'tree', nodeTypes: [{ key: 'node', titleKey: 'Node', pageRef: { pageKey: 'content' } }] };
  const tabs = { key: 'tabs', mode: 'tab', dicKey: 'Test', defaultTabKey: 'tree', tabs: [
    { key: 'tree', titleKey: 'Tree', pageRef: { pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' } },
    { key: 'directory', titleKey: 'Directory', pageRef: { pageKey: 'directory' } },
  ] };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [{ type: 'view', view: tree }, { type: 'view', view: tabs }] }}><PageEntry page={{ pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } }} /></RuntimeMetadataProvider></LocalizationProvider> }]);
  const click = async (selector: string) => { await act(async () => container.querySelector(selector)!.dispatchEvent(new MouseEvent('click', { bubbles: true }))); };
  const clickTab = async (label: string) => act(async () => [...container.querySelectorAll<HTMLElement>('[role=tab]')].find(tab => tab.textContent === label)!.click());
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    for (let i = 0; i < 50 && !container.querySelector('.ant-tree-switcher'); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
    await click('.ant-tree-switcher');
    const title = [...container.querySelectorAll('.ant-tree-title')].find(item => item.textContent === 'd')!;
    await act(async () => title.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await clickTab('Directory');
    children.mockClear();
    await act(async () => [...container.querySelectorAll('button')].find(item => item.textContent === 'move')!.click());
    expect(new URLSearchParams(router.state.location.search).get('tab')).toBe('directory'); expect(children).not.toHaveBeenCalled();
    await clickTab('Tree');
    if (scenario === 'retry') {
      expect(container.textContent).toContain('已保存，刷新失败');
      children.mockClear();
      await act(async () => [...container.querySelectorAll('button')].find(item => item.textContent?.replace(/\s/g, '') === '重试')!.click());
      expect(container.textContent).not.toContain('已保存，刷新失败');
    }
    expect(children.mock.calls.map(call => call[1]).sort()).toEqual(scenario === 'delete' ? ['p1'] : ['p1', 'p2']);
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe(scenario === 'delete' ? 'p1' : 'd');
  } finally { await act(async () => root.unmount()); router.dispose(); container.remove(); }
});
