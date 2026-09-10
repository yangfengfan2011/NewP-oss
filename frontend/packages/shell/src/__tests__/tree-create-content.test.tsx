// @vitest-environment jsdom
import { act, useContext, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, TreeNodeNotFoundError, type PageProps, type TreeProviderBinding } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import { CompositionContext, TreeInstanceContext } from '../composition/pageCompositionRuntime';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

beforeAll(async () => {
  await Promise.all([import('../composition/StandardTreePage'), import('../composition/StandardTabPage')]);
});

it.each([true, false])('rebuilds node content and clears the old node tab after a tree creation, visible=%s', async visible => {
  let created = false;
  const node = (nodeKey: string) => ({ nodeKey, nodeType: 'department', label: nodeKey, hasChildren: false });
  const provider: TreeProviderBinding = {
    key: 'tree',
    roots: async () => created && visible ? [node('root'), node('new')] : [node('root')],
    children: async () => [],
    path: async (_scope, key) => {
      if (key === 'new' && !visible) throw new TreeNodeNotFoundError();
      return [node(key)];
    },
    search: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
    resolveChange: async () => ({ kind: 'reload', operation: 'create', nodeKey: 'new' }),
  };

  function Content({ input }: PageProps) {
    const runtime = useContext(CompositionContext)!;
    const originTreeId = useContext(TreeInstanceContext);
    const [filter, setFilter] = useState(0);
    const name = String(input.parameters.name);
    return <div>
      <output>{input.context?.nodeKey}:{name}:{filter}</output>
      <button onClick={() => setFilter(value => value + 1)}>filter {name}</button>
      <button onClick={async () => {
        created = true;
        await runtime.committed({ resourceKey: 'department', operation: 'create', id: 'new', originTreeId });
      }}>create {name}</button>
    </div>;
  }

  ModuleRegistry.replaceAll([{
    metadata: { moduleId: 'tree-content-test', displayName: 'Tree content test', version: '1' },
    treeProviders: [provider],
    pageInputResolvers: [{ key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }],
    pages: [{ pageKey: 'content', load: async () => ({ default: Content }) }],
  }], 'tree-content-test');
  const tree = { key: 'tree', mode: 'tree', dicKey: 'Tree', providerKey: 'tree', nodeTypes: [{
    key: 'department', titleKey: 'Department', pageRef: { pageKey: 'platform.tabs', parameters: { viewKey: 'tabs' } },
  }] };
  const tabs = { key: 'tabs', mode: 'tab', dicKey: 'Tree', defaultTabKey: 'a', tabs: ['a', 'b'].map(key => ({
    key, titleKey: key, pageRef: { pageKey: 'content', parameters: { name: key } },
  })) };
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
      <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [
        { type: 'view', view: tree }, { type: 'view', view: tabs },
      ] }}>
        <PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' }} />
      </RuntimeMetadataProvider>
    </LocalizationProvider>,
  }], { initialEntries: ['/?node=root&nodeTab=b&external=keep'] });
  onTestFinished(async () => {
    try { await act(async () => root.unmount()); }
    finally { router.dispose(); container.remove(); }
  });
  const clickButton = async (text: string) => {
    const button = [...container.querySelectorAll('button')].find(item => item.textContent === text);
    expect(button, text).toBeDefined();
    await act(async () => button!.click());
  };

  await act(async () => root.render(<RouterProvider router={router} />));
  await clickButton('filter b');
  expect(container.textContent).toContain('root:b:1');
  await clickButton('create b');
  const query = new URLSearchParams(router.state.location.search);
  expect(query.get('node')).toBe(visible ? 'new' : 'root');
  expect(query.has('nodeTab')).toBe(false);
  expect(query.get('external')).toBe('keep');
  expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('a');

  // 被过滤的新节点回退到相同根身份，仍需重建内容；只切回默认标签不足以清除旧实例状态。
  const tabB = [...container.querySelectorAll<HTMLElement>('[role=tab]')].find(item => item.textContent === 'b')!;
  await act(async () => tabB.click());
  expect(container.textContent).toContain(`${visible ? 'new' : 'root'}:b:0`);
  expect(container.textContent).not.toContain('root:b:1');
});
