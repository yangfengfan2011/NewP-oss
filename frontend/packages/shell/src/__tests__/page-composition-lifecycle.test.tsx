// @vitest-environment jsdom
import { act, useEffect, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type PageProps } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
function FormPage({ input, reportState }: PageProps) {
  const [dirty, setDirty] = useState(false);
  const [pending, setPending] = useState(false);
  useEffect(() => reportState({ dirty, submitting: pending }, () => setDirty(false)), [dirty, pending, reportState]);
  return <div><span>{String(input.parameters.name)}:{dirty ? 'dirty' : 'clean'}</span>
    <button onClick={() => setDirty(true)}>change {String(input.parameters.name)}</button>
    <button onClick={() => setPending(value => !value)}>submit {String(input.parameters.name)}</button></div>;
}
let root: Root | undefined;
let container: HTMLDivElement;
afterEach(async () => { await act(async () => root?.unmount()); container?.remove(); });
async function click(text: string) {
  const button = [...document.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === text.replace(/\s/g, ''));
  expect(button, text).toBeDefined();
  await act(async () => button!.dispatchEvent(new MouseEvent('click', { bubbles: true })));
}
async function render() {
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'forms', displayName: 'Forms', version: '1' }, pages: [{ pageKey: 'form', load: async () => ({ default: FormPage }) }] }], 'forms');
  container = document.createElement('div'); document.body.append(container);
  const view = { key: 'tabs', mode: 'tab', dicKey: 'Tabs', defaultTabKey: 'a', tabs: ['a', 'b'].map(key => ({ key, titleKey: key, pageRef: { pageKey: 'form', parameters: { name: key } } })) };
  await act(async () => { root = createRoot(container); root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: {} }}>
    <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [{ type: 'view', view }] }}>
      <PageEntry page={{ pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } }} />
    </RuntimeMetadataProvider></LocalizationProvider>); });
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
});
