// @vitest-environment jsdom
import { act, StrictMode, useEffect, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { createMemoryRouter, Link, RouterProvider, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type PageProps } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { ShellRouter } from '../navigation/ShellRouter';
import { PageEntry } from '../composition/PageOutlet';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
function FormPage({ input, reportState }: PageProps) {
  const navigate = useNavigate();
  const [dirty, setDirty] = useState(false);
  const [pending, setPending] = useState(false);
  const [filter, setFilter] = useState(0);
  useEffect(() => reportState({ dirty, submitting: pending }, () => setDirty(false)), [dirty, pending, reportState]);
  return <div><span>{String(input.parameters.name)}:{dirty ? 'dirty' : 'clean'}</span>
    <Link to="/other">outside</Link><button onClick={() => navigate(-1)}>back</button>
    <button onClick={() => setDirty(true)}>change {String(input.parameters.name)}</button>
    <button onClick={() => setFilter(value => value + 1)}>filter {String(input.parameters.name)}</button><span>{input.context?.nodeKey}:{filter}</span>
    <button onClick={() => setPending(value => !value)}>submit {String(input.parameters.name)}</button></div>;
}
let router: ReturnType<typeof createMemoryRouter>;
let root: Root | undefined;
let container: HTMLDivElement;
afterEach(async () => { await act(async () => root?.unmount()); container?.remove(); router?.dispose(); vi.unstubAllGlobals(); });
async function click(text: string) {
  const button = [...document.querySelectorAll('button, [role=tab]')].find(button => button.textContent?.replace(/\s/g, '') === text.replace(/\s/g, ''));
  expect(button, text).toBeDefined();
  await act(async () => button!.dispatchEvent(new MouseEvent('click', { bubbles: true })));
}
async function render(browser = false, search = '', tree = false) {
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'forms', displayName: 'Forms', version: '1' }, pages: [{ pageKey: 'form', load: async () => ({ default: FormPage }) }],
    pageInputResolvers: [{ key: 'global', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }],
    treeProviders: [{ key: 'tree', roots: async () => ['one', 'two', 'other'].map(key => ({ nodeKey: key, nodeType: key === 'other' ? 'other' : 'node', label: key, hasChildren: false })), children: async () => [], path: async () => [], search: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }), resolveChange: async () => ({ kind: 'unrelated' }) }],
  }], 'forms');
  container = document.createElement('div'); document.body.append(container);
  const view = { key: 'tabs', mode: 'tab', dicKey: 'Tabs', defaultTabKey: 'a', tabs: ['a', 'b'].map(key => ({ key, titleKey: key, pageRef: { pageKey: 'form', parameters: { name: key } } })) };
  const element = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: {} }}>
    <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [{ type: 'view', view },
      { type: 'view', view: { ...view, key: 'other-tabs' } },
      { type: 'view', view: { key: 'tree', mode: 'tree', dicKey: 'Tabs', providerKey: 'tree', nodeTypes: ['node', 'other'].map(key => ({ key, titleKey: key, contextMenu: [{ key: 'edit', labelKey: 'Edit', standardAction: 'edit', viewKey: 'list', inputResolverKey: 'node' }], pageRef: { pageKey: 'platform.tabs', parameters: { viewKey: key === 'node' ? 'tabs' : 'other-tabs' } } })) } },
    ] }}>
      <PageEntry page={tree ? { pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'global' } : { pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } }} />
    </RuntimeMetadataProvider></LocalizationProvider>;
  router = createMemoryRouter([{ path: '/structure', element }, { path: '/other', element: <p>outside page</p> }, { path: '/previous', element: <p>previous page</p> }], { initialEntries: ['/previous', `/structure${search}`] });
  await act(async () => { root = createRoot(container); root.render(browser ? <StrictMode><ShellRouter><Routes><Route path="/structure" element={element} /><Route path="/other" element={<p>outside page</p>} /></Routes></ShellRouter></StrictMode> : <RouterProvider router={router} />); });
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 30)); });
}
describe('whole composition lifecycle', () => {
  it('keeps the active tab within a node group, resets per-node filters, and defaults after a group change without replacing unrelated outer URL state', async () => {
    await render(false, '?external=keep', true);
    for (let i = 0; i < 30 && !container.querySelector('[role=tab]'); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
    await click('b'); await click('filter b');
    expect(container.textContent).toContain('one:1');
    await click('a'); await click('b');
    expect(container.textContent).toContain('one:1');
    const selectNode = async (name: string) => {
      const title = [...container.querySelectorAll('.ant-tree-title')].find(node => node.textContent === name)!;
      await act(async () => title.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    };
    await selectNode('two');
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('b');
    expect(container.textContent).toContain('two:0');
    expect(container.textContent).not.toContain('one:1');
    await selectNode('other');
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('a');
    expect(new URLSearchParams(router.state.location.search).get('external')).toBe('keep');
  });
  it('selects a right-click target only after dirty-input protection, then opens its menu', async () => {
    await render(false, '', true);
    for (let i = 0; i < 30 && !container.querySelector('[role=tab]'); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
    await click('change a');
    const target = [...container.querySelectorAll('.ant-tree-title')].find(node => node.textContent === 'two')!;
    await act(async () => target.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true })));
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('one');
    expect(document.querySelector('[role=menu]')).toBeNull();
    await click('继续编辑');
    expect(container.textContent).toContain('a:dirty');
    await act(async () => target.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true })));
    await click('放弃修改');
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('two');
    expect(document.querySelector('[role=menu]')?.textContent).toContain('Edit');
  });
  it('loads only the requested root tab, isolates identical custom page instances and preserves opened content on history restoration', async () => {
    await render(false, '?tab=b&other=keep');
    expect(container.textContent).toContain('b:clean');
    expect(container.textContent).not.toContain('a:clean');
    await click('a');
    expect(router.state.location.search).toBe('?tab=a&other=keep');
    await act(async () => router.navigate(-1));
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('b');
    expect(container.textContent).toContain('a:clean');
    await click('change a');
    expect(container.textContent).toContain('a:dirty');
    expect(container.textContent).toContain('b:clean');
    await act(async () => router.navigate('/other'));
    await click('继续编辑');
    expect(router.state.location.search).toBe('?tab=b&other=keep');
    expect(container.textContent).toContain('a:dirty');
  });
  it('cancels leaving without losing inputs, discards once on confirmation, and blocks a submitting page', async () => {
    await render();
    await click('change a'); await click('b');
    expect(document.body.textContent).toContain('放弃未保存的修改');
    await click('继续编辑');
    expect(container.textContent).toContain('a:dirty');
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('a');
    await click('b'); await click('放弃修改');
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('b');
    expect(container.textContent).toContain('a:clean');
    await click('submit b'); await click('a');
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('b');
  });
  it('keeps opened inactive custom content in the aggregate protection scope', async () => {
    await render(); await click('b');
    await click('change a'); // a mounted but inactive page may report an asynchronous edit.
    await click('a');
    expect(document.body.textContent).toContain('放弃未保存的修改');
    await click('继续编辑');
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('b');
    expect(container.textContent).toContain('a:dirty');
  });
  it('protects Link navigation with one shared decision and preserves route and input on cancel', async () => {
    await render(); await click('change a');
    await act(async () => container.querySelector('a')!.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0, cancelable: true })));
    expect(router.state.location.pathname).toBe('/structure');
    expect(document.querySelectorAll('.ant-modal')).toHaveLength(1);
    await click('继续编辑');
    expect(container.textContent).toContain('a:dirty');
    expect(router.state.location.pathname).toBe('/structure');
    await act(async () => router.navigate('/other'));
    await click('放弃修改');
    expect(router.state.location.pathname).toBe('/other');
    expect(container.textContent).toContain('outside page');
  });
  it('protects history back and refuses SPA navigation during submission', async () => {
    await render(); await click('change a'); await click('back');
    expect(router.state.location.pathname).toBe('/structure');
    await click('继续编辑');
    await click('submit a');
    await act(async () => router.navigate('/other'));
    expect(router.state.location.pathname).toBe('/structure');
    expect(document.querySelector('.ant-modal-wrap:not([style*="display: none"])')).toBeNull();
    await click('submit a'); await click('back'); await click('放弃修改');
    expect(router.state.location.pathname).toBe('/previous');
  });

  it('preserves browser route and input until confirmation under StrictMode', async () => {
    window.history.replaceState(null, '', '/structure');
    await render(true); await click('change a');
    await act(async () => container.querySelector('a')!.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0, cancelable: true })));
    expect(window.location.pathname).toBe('/structure');
    await click('继续编辑');
    expect(container.textContent).toContain('a:dirty');
    await act(async () => container.querySelector('a')!.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0, cancelable: true })));
    await click('放弃修改');
    expect(window.location.pathname).toBe('/other');
    expect(container.textContent).toContain('outside page');
    window.history.replaceState(null, '', '/');
  });

});
