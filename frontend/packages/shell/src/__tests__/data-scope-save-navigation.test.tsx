// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { Link, Route, Routes } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { ModuleRegistry } from '@icp-platform/sdk';
import { PageEntry } from '../composition/PageOutlet';
import { ShellRouter } from '../navigation/ShellRouter';
import DataScopeRulesPage from '../../../../modules/platform/authorization/src/DataScopeRulesPage';

const fetcher = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', async importOriginal => ({ ...await importOriginal<object>(),
  platformFetch: fetcher, useAuthorization: () => ({ hasFeature: () => true }) }));

it('can navigate again after saving a new data-scope rule from the leave dialog', async () => {
  const click = async (label: string) => {
    const item = [...document.querySelectorAll('button, a')].find(item => item.textContent?.replace(/\s/g, '') === label.replace(/\s/g, '')) as HTMLElement;
    expect(item, label).toBeDefined();
    await act(async () => item.click());
  };
  await import('../composition/StandardTreePage');
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'navigation-test', displayName: 'Navigation', version: '1' }, pages: [
    { pageKey: 'rules', load: async () => ({ default: DataScopeRulesPage }) },
  ], pageInputResolvers: [{ key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }],
    treeProviders: [{ key: 'tree', roots: async () => [], children: async () => [], path: async () => [], search: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }), resolveChange: async () => ({ kind: 'unrelated' }) }],
  }], 'navigation-test');
  let finish!: () => void;
  fetcher.mockImplementation((url: string, init?: RequestInit) => {
    if (url.startsWith('/plugins/')) return Promise.resolve(new Response(JSON.stringify({ modules: [] })));
    if (init?.method === 'PUT') return new Promise(resolve => { finish = () => resolve(new Response(null, { status: 204 })); });
    return Promise.resolve(new Response(JSON.stringify(url.endsWith('/catalog') ? {
      actions: [{ key: 'read', title: '读取', enabled: true }],
      features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
      buttons: [{ id: 'rules-save', moduleId: 'Authorization', viewKey: 'feature:rules.save', surface: 'featureActions', actionKey: 'save', execution: '', resources: [], actions: [], featurePositions: ['save'], businessBinding: 'authorization.dataScope.save' }],
      resources: [{ resource: { resourceKey: 'organization.person', moduleId: 'Organization', modelName: 'Person', modelKey: 'Person' }, fields: [{ name: 'ENAME', type: 'String' }] }],
    } : [])));
  });
  window.history.replaceState(null, '', '/rules');
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: {} }}>
      <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [], viewDocuments: [
        { type: 'view', view: { key: 'tree', mode: 'tree', dicKey: 'Tree', providerKey: 'tree', nodeTypes: [] } },
      ] }}><ShellRouter><Link to="/next">切换导航</Link><Link to="/other">再次导航</Link><Routes>
      <Route path="/rules" element={<PageEntry page={{ pageKey: 'rules', parameters: { featurePosition: 'read' } }} />} />
      <Route path="/next" element={<PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' }} />} />
      <Route path="/other" element={<p>后续页面</p>} />
    </Routes></ShellRouter></RuntimeMetadataProvider></LocalizationProvider>));
    await click('＋ 新增规则');
    await click('切换导航');
    await click('保存并离开');
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(1);
    await act(async () => finish());
    expect(window.location.pathname).toBe('/next');
    expect(container.textContent).toContain('暂无节点');
    const beforeUnload = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(beforeUnload);
    expect(beforeUnload.defaultPrevented, '已保存并进入只读树页，不应残留离开拦截').toBe(false);
    await click('再次导航');
    expect(window.location.pathname).toBe('/other');
    expect(container.textContent).toContain('后续页面');
  } finally { await act(async () => root.unmount()); container.remove(); window.history.replaceState(null, '', '/'); }
});
