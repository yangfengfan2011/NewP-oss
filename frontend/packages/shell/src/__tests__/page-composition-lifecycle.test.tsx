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
  useEffect(() => reportState({ dirty, submitting: pending }, () => setDirty(false)), [dirty, pending, reportState]);
  return <div><span>{String(input.parameters.name)}:{dirty ? 'dirty' : 'clean'}</span>
    <Link to="/other">outside</Link><button onClick={() => navigate(-1)}>back</button>
    <button onClick={() => setDirty(true)}>change {String(input.parameters.name)}</button>
    <button onClick={() => setPending(value => !value)}>submit {String(input.parameters.name)}</button></div>;
}
let router: ReturnType<typeof createMemoryRouter>;
let root: Root | undefined;
let container: HTMLDivElement;
afterEach(async () => { await act(async () => root?.unmount()); container?.remove(); router?.dispose(); vi.unstubAllGlobals(); });
async function click(text: string) {
  const button = [...document.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === text.replace(/\s/g, ''));
  expect(button, text).toBeDefined();
  await act(async () => button!.dispatchEvent(new MouseEvent('click', { bubbles: true })));
}
async function render(browser = false) {
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'forms', displayName: 'Forms', version: '1' }, pages: [{ pageKey: 'form', load: async () => ({ default: FormPage }) }] }], 'forms');
  container = document.createElement('div'); document.body.append(container);
  const view = { key: 'tabs', mode: 'tab', dicKey: 'Tabs', defaultTabKey: 'a', tabs: ['a', 'b'].map(key => ({ key, titleKey: key, pageRef: { pageKey: 'form', parameters: { name: key } } })) };
  const element = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: {} }}>
    <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [{ type: 'view', view }] }}>
      <PageEntry page={{ pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } }} />
    </RuntimeMetadataProvider></LocalizationProvider>;
  router = createMemoryRouter([{ path: '/structure', element }, { path: '/other', element: <p>outside page</p> }, { path: '/previous', element: <p>previous page</p> }], { initialEntries: ['/previous', '/structure'] });
  await act(async () => { root = createRoot(container); root.render(browser ? <StrictMode><ShellRouter><Routes><Route path="/structure" element={element} /><Route path="/other" element={<p>outside page</p>} /></Routes></ShellRouter></StrictMode> : <RouterProvider router={router} />); });
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 30)); });
}
describe('whole composition lifecycle', () => {
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
