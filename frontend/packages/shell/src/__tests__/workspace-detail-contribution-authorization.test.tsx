// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import workspace from '../../../../modules/workspace/src';
import part from '../../../../modules/part/src';
import documentModule from '../../../../modules/document/src';
import { partResource } from '../../../../modules/part/src/partResource';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import partModel from '../../../../../tests/Kernel.Tests/Fixtures/PartRuntime.contract.json';
import documentModel from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import content from '../../../../../modules/Workspace/metadata/models/WorkspaceFolderContent.model.json';
import references from '../../../../../modules/Workspace/metadata/views/WorkspaceReferencesList.view.json';
import detail from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderDetail.view.json';
import list from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderList.view.json';
import partDetail from '../../../../../modules/Part/metadata/views/PartDetail.view.json';
import partList from '../../../../../modules/Part/metadata/views/PartList.view.json';
import documentDetail from '../../../../../modules/Document/metadata/views/DocumentDetail.view.json';
import documentList from '../../../../../modules/Document/metadata/views/DocumentList.view.json';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { AuthProvider } from '../contexts/AuthContext';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';

// 收集期装载真实模块，避免冷转换耗时进入生命周期 hook 的交互预算。
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListDrawerRuntime';
import '@ant-design/pro-descriptions';
import '../../../../modules/workspace/src/workspaceReferencesResource';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

it('keeps owning details available without Workspace and discards an old reverse read after the object changes', async () => {
  const user = { id: 'actor', ecode: 'actor', ename: '账号', revision: 1, mustChangePassword: false, isLocalAdministrator: false,
    timeZoneId: 'Asia/Taipei', featurePositions: ['part-entry'] };
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const detailFor = (id: string) => ({ fields: { EID: id, ECODE: id, ENAME: id + '-properties', Description: '原详情',
    CreatedBy: 'actor', CreatedBy_DisplayValue: '用户', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '用户',
    CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' }, summaries: {} });
  let finishOld: ((response: Response) => void) | undefined;
  const reverseIds: string[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.startsWith('/api/part/parts/')) {
      expect(new Headers(input instanceof Request ? input.headers : init?.headers).get('X-NewP-Feature')).toBe('part-entry');
      return json(detailFor(url.pathname.split('/').at(-1)!));
    }
    if (url.pathname === '/api/links/workspace.content') {
      reverseIds.push(url.searchParams.get('anchorId')!);
      if (url.searchParams.get('anchorId') === 'first') return new Promise<Response>(resolve => { finishOld = resolve; });
      return json({ items: [{ linkId: 'second-incoming', associatedObject: { modelKey: 'Workspace/WorkspaceFolder', id: 'second-parent' }, fields: { LID: 'second-incoming', Name: '第二对象的父' } }], total: 1, page: 1, pageSize: 20 });
    }
    throw new Error('Unexpected request ' + url.pathname);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ ...workspace, metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' } },
    { ...part, metadata: { moduleId: 'Part', displayName: '零组件', version: '1' } },
    { ...documentModule, metadata: { moduleId: 'Document', displayName: '图文档', version: '1' } }], 'contribution-auth');
  const metadata = { manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
    features: [{ key: 'workspace', position: 'workspace-entry', kind: 'navigation' }] }] },
    navDocuments: [{ type: 'nav' as const, navigation: { items: [{ key: 'workspace', featurePosition: 'workspace-entry' }] } }],
    viewDocuments: [references, detail, list, partDetail, partList, documentDetail, documentList], modelDocuments: [folder, content, partModel, documentModel] };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  function Target() { const { id } = useParams(); return <StandardCrudDetailPageHost viewKey="part.part.detail" id={id ?? null} registration={partResource} />; }
  const router = createMemoryRouter([{ path: '/:id', element: <Target /> }], { initialEntries: ['/first'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'contribution-auth-cleanup'); });
  const render = async (id: string, qualified: boolean) => act(async () => {
    root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={qualified ? ['part-entry', 'workspace-entry'] : ['part-entry']}><PlatformQueryProvider scopeKey="contribution-auth">
      <RuntimeMetadataProvider value={metadata}><FeaturePlacementContext.Provider value={{ entryPosition: 'part-entry' }}>
        <RouterProvider router={router} />
      </FeaturePlacementContext.Provider></RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>);
    if (router.state.location.pathname !== '/' + id) await router.navigate('/' + id);
  });
  const entryButton = () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === 'Workspace.DirectReferences');
  await render('first', false);
  await vi.waitFor(() => expect(container.textContent).toContain('first-properties'));
  expect(entryButton()).toBeUndefined(); expect(reverseIds).toEqual([]);
  await render('first', true);
  await vi.waitFor(() => expect(entryButton()).not.toBeUndefined());
  await act(async () => entryButton()!.click());
  await vi.waitFor(() => expect(reverseIds).toEqual(['first']));
  await render('second', true);
  await vi.waitFor(() => expect(container.textContent).toContain('second-properties'));
  await vi.waitFor(() => expect(document.querySelector('.ant-modal')).toBeNull());
  await act(async () => entryButton()!.click());
  await vi.waitFor(() => expect(document.querySelector('tr[data-row-key="second-incoming"]')).not.toBeNull());
  await act(async () => finishOld!(json({ items: [{ linkId: 'old-private', associatedObject: { modelKey: 'Workspace/WorkspaceFolder', id: 'old' }, fields: { LID: 'old-private', Name: '旧私有父' } }], total: 1, page: 1, pageSize: 20 })));
  expect(document.body.textContent).not.toContain('旧私有父');
  await render('second', false);
  await vi.waitFor(() => expect(document.querySelector('.ant-modal')).toBeNull());
  expect(container.textContent).toContain('second-properties'); expect(entryButton()).toBeUndefined();
  await render('second', true);
  await vi.waitFor(() => expect(entryButton()).not.toBeUndefined());
  expect(document.querySelector('.ant-modal')).toBeNull(); expect(reverseIds).toEqual(['first', 'second']);
});
