// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, TreeNodeNotFoundError, type PageProps, type TreeProviderBinding } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
it.each(['restore', 'offline', 'missing', 'search-moved', 'search-deleted', 'stale', 'branch-retry', 'root-retry', 'empty'])('restores current ancestors and node tab, distinguishing %s', async scenario => {
  const node = (key: string) => ({ nodeKey: key, nodeType: 'n', label: key, hasChildren: key !== 'd' });
  let offline = scenario === 'offline';
  let failRoot = scenario === 'root-retry'; let failBranch = scenario === 'branch-retry';
  let finishPath!: (path: ReturnType<typeof node>[]) => void;
  const provider: TreeProviderBinding = { key: 'tree', roots: vi.fn(async () => { if (failRoot) throw new Error('roots offline'); return scenario === 'empty' ? [] : [node('c'), node('other')]; }), children: vi.fn(async () => { if (failBranch) throw new Error('branch offline'); return [node('d')]; }),
    path: vi.fn(async () => { if (offline) throw new Error('offline'); if (scenario === 'missing' || scenario === 'search-deleted') throw new TreeNodeNotFoundError(); if (scenario === 'stale') return new Promise(resolve => { finishPath = resolve; }); return [node(scenario === 'search-moved' ? 'other' : 'c'), node('d')]; }),
    search: vi.fn(async () => ({ items: [{ node: node('d'), path: [node('c'), node('d')] }], total: 1, page: 1, pageSize: 20 })), resolveChange: vi.fn() };
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'Test', version: '1' }, treeProviders: [provider], pageInputResolvers: [{ key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }], pages: [{ pageKey: 'content', load: async () => ({ default: ({ input }: PageProps) => <p>{input.context?.nodeKey}:{String(input.parameters.name)}</p> }) }] }], 'test');
  const tree = { key: 'tree', mode: 'tree', dicKey: 'Test', providerKey: 'tree', nodeTypes: [{ key: 'n', titleKey: 'Node', pageRef: { pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } } }] };
  const tabs = { key: 'tabs', mode: 'tab', dicKey: 'Test', defaultTabKey: 'basic', tabs: [{ key: 'basic', titleKey: 'Basic', pageRef: { pageKey: 'content', parameters: { name: 'basic' } } }, { key: 'children', titleKey: 'Children', pageRef: { pageKey: 'content', parameters: { name: 'children' } } }] };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const outer = { key: 'outer', mode: 'tab', dicKey: 'Test', defaultTabKey: 'structure', tabs: [{ key: 'structure', titleKey: 'Structure', pageRef: { pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' } }] };
  const initial = ['restore', 'offline', 'missing'].includes(scenario) ? '/?tab=structure&node=d&nodeTab=children' : '/?tab=structure';
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [{ type: 'view', view: tree }, { type: 'view', view: tabs }, { type: 'view', view: outer }] }}><PageEntry page={{ pageKey: 'platform.tabs', parameters: { viewKey: 'outer' } }} /></RuntimeMetadataProvider></LocalizationProvider> }], { initialEntries: [initial] });
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    for (let i = 0; i < 50 && !container.querySelector('aside'); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
    if (scenario === 'empty') { expect(container.textContent).toContain('暂无节点'); expect(provider.children).not.toHaveBeenCalled(); return; }
    if (scenario === 'root-retry') { expect(container.textContent).toContain('树读取失败：操作失败，请稍后重试。'); expect(container.textContent).not.toContain('roots offline'); expect(container.textContent).not.toContain('暂无节点'); failRoot = false; await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '重试')!.click()); expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('c'); return; }
    if (scenario === 'branch-retry') { await act(async () => container.querySelector<HTMLElement>('.ant-tree-switcher')!.click()); expect(container.textContent).toContain('分支读取失败'); failBranch = false; await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '重试分支')!.click()); expect(container.textContent).not.toContain('分支读取失败'); expect(container.textContent).toContain('d'); return; }
    if (scenario.startsWith('search') || scenario === 'stale') {
      await act(async () => { const input = container.querySelector('input')!; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'd'); input.dispatchEvent(new Event('input', { bubbles: true })); });
      expect(provider.path).not.toHaveBeenCalled();
      await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="定位节点 d"]')!.click());
      expect(provider.path).toHaveBeenCalledWith(expect.anything(), 'd', expect.any(AbortSignal));
      if (scenario === 'stale') { await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === 'other')!.click()); await act(async () => finishPath([node('c'), node('d')])); expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('other'); return; }
      expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe(scenario === 'search-deleted' ? 'c' : 'd');
      if (scenario === 'search-deleted') expect(container.textContent).toContain('目标节点已不存在');
      if (scenario === 'search-moved') expect(provider.children).toHaveBeenCalledWith(expect.anything(), 'other', expect.any(AbortSignal));
      return;
    }
    if (scenario === 'offline') { expect(container.textContent).toContain('定位读取失败：操作失败，请稍后重试。'); expect(container.textContent).not.toContain('offline'); expect(container.querySelector('.ant-tree-node-selected')).toBeNull(); offline = false; await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '重试定位')!.click()); }
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe(scenario === 'missing' ? 'c' : 'd');
    if (scenario === 'missing') expect(container.textContent).toContain('目标节点已不存在');
    for (let i = 0; i < 50 && !container.textContent?.includes('d:children'); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
    if (scenario !== 'missing') expect(container.querySelector('[role=tabpanel]:not([hidden])')?.textContent).toContain('d:children');
  } finally { await act(async () => root.unmount()); router.dispose(); container.remove(); }
});
