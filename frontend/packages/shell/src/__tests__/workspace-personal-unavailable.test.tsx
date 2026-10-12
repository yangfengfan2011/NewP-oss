// @vitest-environment jsdom
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, invalidatePlatformAntiforgeryToken, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';

// 收集期装载真实模块，避免冷转换耗时进入生命周期 hook 的交互预算。
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
await loadComponentSource('Workspace', 'workspaceRootResource.ts');

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

it.each([
  ['zhs', '个人空间尚未初始化或不可读取，请重试或联系管理员。'],
  ['en', 'Personal space is not initialized or cannot be read. Retry or contact an administrator.'],
])('shows a controlled personal-space unavailable message in %s after a successful receipt', async (language, message) => {
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    const method = input instanceof Request ? input.method : init?.method;
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('/personal/ensure') && method === 'POST') return json({ initialized: true });
    if (url.pathname === '/api/workspace/spaces') return json([{ spaceId: 'public', spaceKind: 'Public', rootFolderId: 'public-root', name: '公共空间' }]);
    if (url.pathname.endsWith('/path')) return json({ folderId: 'public-root', folderIds: ['public-root'], names: ['公共空间'] });
    if (url.pathname.startsWith('/api/links/')) return json({ items: [], total: 0, page: 1, pageSize: 20 });
    throw new Error(`Unexpected request ${method} ${url.pathname}`);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace }], 'workspace-unavailable');
  const catalogs = Object.fromEntries(['zhs', 'en'].map(code => [code, Object.fromEntries(Object.entries(dictionary.Workspace)
    .map(([key, value]) => [`Workspace.${key}`, value[code as 'zhs' | 'en']]))]));
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage={language} supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]} catalogs={catalogs}>
      <AuthorizationProvider featurePositions={['workspace-entry']}><PlatformQueryProvider scopeKey="workspace-unavailable">
        <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [folder, content], viewDocuments: [tree, list, detail] }}>
          <PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'workspace-entry' }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider>
    </LocalizationProvider>,
  }], { initialEntries: ['/workspace'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); ModuleRegistry.replaceAll([], 'workspace-unavailable-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent ?? '').toContain(message));
  expect(container.textContent).not.toContain('public-root');
  expect(container.querySelector('.ant-tree-title')).toBeNull();
  await act(async () => container.querySelector<HTMLButtonElement>('[role="alert"] button')!.click());
  expect(fetcher.mock.calls.filter(([input]) => new URL(input instanceof Request ? input.url : String(input), 'http://localhost').pathname === '/api/workspace/spaces')).toHaveLength(2);
  const writes = fetcher.mock.calls.filter(([input, init]) => (input instanceof Request ? input.method : init?.method) === 'POST');
  expect(writes).toHaveLength(1);
});
