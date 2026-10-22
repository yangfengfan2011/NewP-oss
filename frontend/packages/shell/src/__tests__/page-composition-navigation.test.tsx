// @vitest-environment jsdom
import { act, useContext, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type PageProps, type PageRef } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry, PageOutlet } from '../composition/PageOutlet';
import { PageActivityContext, PageNavigationContext, TreeNavigationContext } from '../composition/pageCompositionRuntime';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

beforeAll(async () => { await import('../composition/StandardTabPage'); });

function NavigationPage({ input, active, reportState }: PageProps) {
  const navigation = useContext(PageNavigationContext);
  const treeNavigation = useContext(TreeNavigationContext);
  const [dirty, setDirty] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [discardCount, setDiscardCount] = useState(0);
  useEffect(() => reportState({ dirty, submitting }, () => { setDirty(false); setDiscardCount(value => value + 1); }), [dirty, submitting, reportState]);
  return <section aria-label={String(input.parameters.name ?? 'root')}>
    <output>{treeNavigation?.nodeKey ?? 'none'}:{treeNavigation?.nodeTab ?? 'none'}</output>
    <span>{active ? 'visible' : 'hidden'}:{dirty ? 'dirty' : 'clean'}</span>
    <span>discards:{discardCount}</span>
    <input aria-label="local input" />
    <button onClick={() => setDirty(true)}>edit</button>
    <button onClick={() => setSubmitting(value => !value)}>toggle submitting</button>
    <button onClick={() => treeNavigation?.locate('b')}>locate and preserve</button>
    <button onClick={() => treeNavigation?.locate('b', null)}>locate and clear</button>
    <button onClick={() => treeNavigation?.locate(null)}>clear node only</button>
    <button onClick={() => treeNavigation?.locate('b', '')}>locate with empty tab</button>
    <button onClick={() => navigation?.selectTab('B')}>select and preserve</button>
    <button onClick={() => navigation?.selectTab('C', { nodeKey: 'c', nodeTab: 'basic' })}>handoff</button>
    <button onClick={() => navigation?.selectTab('D', { nodeKey: null, nodeTab: null })}>empty handoff</button>
  </section>;
}

function LocalTabsPage() {
  return <PageOutlet page={{ pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } }} />;
}

const tabsPage: PageRef = { pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } };

async function mount(address: string, page: PageRef = { pageKey: 'navigation' }, parentActive = true) {
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'navigation-test', displayName: 'Navigation test', version: '1' },
    pages: [{ pageKey: 'navigation', load: async () => ({ default: NavigationPage }) }, { pageKey: 'local-tabs', load: async () => ({ default: LocalTabsPage }) }],
  }], 'navigation-test');
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/structure', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
      <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [{ type: 'view', view: {
        key: 'tabs', mode: 'tab', dicKey: 'Tabs', defaultTabKey: 'A', tabs: ['A', 'B'].map(key => ({ key, titleKey: key, pageRef: { pageKey: 'navigation', parameters: { name: key } } })),
      } }] }}>
        <PageActivityContext.Provider value={parentActive}><PageEntry page={page} /></PageActivityContext.Provider>
      </RuntimeMetadataProvider>
    </LocalizationProvider>,
  }], { initialEntries: [address] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'navigation-test-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  const click = async (label: string, scope: ParentNode = document) => {
    const target = [...scope.querySelectorAll<HTMLElement>('button, [role=tab]')].find(item => item.textContent?.replace(/\s/g, '') === label.replace(/\s/g, ''));
    expect(target, label).toBeDefined();
    await act(async () => target!.click());
  };
  return { container, router, click };
}

it('preserves or clears location commands through PageEntry without adding history for unchanged locations', async () => {
  const { router, click } = await mount('/structure?tab=A&node=a&nodeTab=children&other=keep');
  await click('select and preserve');
  expect(router.state.location.search).toBe('?tab=B&node=a&nodeTab=children&other=keep');
  expect(router.state.historyAction).toBe('PUSH');
  await click('locate and preserve');
  expect(router.state.location.search).toBe('?tab=B&node=b&nodeTab=children&other=keep');
  expect(router.state.historyAction).toBe('REPLACE');
  const replacedKey = router.state.location.key;
  await click('locate and preserve');
  expect(router.state.location.key).toBe(replacedKey);
  await click('clear node only');
  expect(router.state.location.search).toBe('?tab=B&nodeTab=children&other=keep');
  await click('locate and clear');
  expect(router.state.location.search).toBe('?tab=B&other=keep&node=b');
  const addresses: string[] = [];
  const unsubscribe = router.subscribe(state => addresses.push(state.location.search));
  await click('handoff');
  unsubscribe();
  expect(addresses).toEqual(['?tab=C&other=keep&node=c&nodeTab=basic']);
  expect(router.state.location.pathname).toBe('/structure');
  await click('empty handoff');
  expect(router.state.location.search).toBe('?tab=D&other=keep');
  await act(async () => router.navigate(-1));
  expect(router.state.location.search).toBe('?tab=C&other=keep&node=c&nodeTab=basic');
});

it.each([true, false])('remembers inactive tab locations and hands off an unvisited tab atomically, parent visible=%s', async parentActive => {
  const { container, router, click } = await mount('/structure?tab=A&node=a&nodeTab=children&other=keep', tabsPage, parentActive);
  const tabA = container.querySelector<HTMLElement>('section[aria-label=A]')!;
  expect(tabA.textContent).toContain(parentActive ? 'visible:clean' : 'hidden:clean');
  const inputA = tabA.querySelector<HTMLInputElement>('input')!;
  inputA.value = 'keep A';
  const addresses: string[] = [];
  const unsubscribe = router.subscribe(state => addresses.push(state.location.search));
  onTestFinished(unsubscribe);
  await click('B');
  expect(addresses).toEqual(['?tab=B&other=keep']);
  expect(container.querySelector('section[aria-label=B] output')?.textContent).toBe('none:none');
  const activeAddressKey = router.state.location.key;
  // 隐藏内容仍可异步报告自己的位置，但不能消费或改写兄弟的地址。
  await click('locate and preserve', tabA);
  expect(router.state.location.key).toBe(activeAddressKey);
  expect(router.state.location.search).toBe('?tab=B&other=keep');
  await click('A');
  expect(addresses).toEqual(['?tab=B&other=keep', '?tab=A&other=keep&node=b&nodeTab=children']);
  expect(tabA.querySelector('output')?.textContent).toBe('b:children');
  expect(inputA.value).toBe('keep A');
  await click('locate and clear', tabA);
  expect(router.state.location.search).toBe('?tab=A&other=keep&node=b');
  expect(router.state.historyAction).toBe('REPLACE');
  await click('B'); await click('A');
  expect(tabA.querySelector('output')?.textContent).toBe('b:none');
});

it.each(['?other=keep', '?tab=unknown&other=keep'])('defaults the selected tab without normalizing the address %s', async search => {
  const { container, router } = await mount(`/structure${search}`, tabsPage);
  expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('A');
  expect(container.querySelector('section[aria-label=B]')).toBeNull();
  expect(router.state.location.search).toBe(search);
  expect(router.state.historyAction).toBe('POP');
});

it('preserves the existing empty-string node-tab command at the address adapter', async () => {
  const { router, click } = await mount('/structure?tab=A&node=a&nodeTab=children&other=keep');
  await click('locate with empty tab');
  expect(router.state.location.search).toBe('?tab=A&node=b&nodeTab=children&other=keep');
});

it('keeps ordinary embedded tab selection local to its PageOutlet', async () => {
  const { container, router, click } = await mount('/structure?tab=outer&node=a&nodeTab=children&other=keep', { pageKey: 'local-tabs' });
  const addressKey = router.state.location.key;
  expect(container.querySelector('output')?.textContent).toBe('none:none');
  await click('B');
  expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('B');
  await click('locate and clear', container.querySelector('section[aria-label=B]')!);
  expect(router.state.location.key).toBe(addressKey);
  expect(router.state.location.search).toBe('?tab=outer&node=a&nodeTab=children&other=keep');
});

it('leaves the entire target handoff pending until one root protection decision approves it', async () => {
  const { container, router, click } = await mount('/structure?tab=A&node=a&nodeTab=children&other=keep', tabsPage);
  await click('B');
  const tabB = container.querySelector('section[aria-label=B]')!;
  await click('locate and clear', tabB);
  await click('A');
  const tabA = container.querySelector('section[aria-label=A]')!;
  const inputA = tabA.querySelector<HTMLInputElement>('input')!;
  inputA.value = 'unsaved A';
  await click('edit', tabA);
  const oldAddress = router.state.location;
  await click('B');
  expect(document.querySelectorAll('.ant-modal')).toHaveLength(1);
  expect(router.state.location).toEqual(oldAddress);
  expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('A');
  await click('继续编辑');
  expect(inputA.value).toBe('unsaved A');
  expect(tabA.textContent).toContain('dirty');
  await click('B'); await click('放弃修改');
  expect(router.state.location.search).toBe('?tab=B&other=keep&node=b');
  expect(tabA.textContent).toContain('clean');
  expect(tabA.textContent).toContain('discards:1');
  await click('toggle submitting', tabB);
  const submittingAddress = router.state.location;
  await click('A');
  expect(router.state.location).toEqual(submittingAddress);
  expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('B');
});
