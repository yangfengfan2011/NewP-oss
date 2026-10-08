// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import workspace from '../../../../modules/workspace/src';
import part from '../../../../modules/part/src';
import documentModule from '../../../../modules/document/src';
import resource from '../../../../modules/workspace/src/workspaceRootResource';
import { partResource } from '../../../../modules/part/src/partResource';
import { documentResource } from '../../../../modules/document/src/documentResource';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import partModel from '../../../../../tests/Kernel.Tests/Fixtures/PartRuntime.contract.json';
import documentModel from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import content from '../../../../../modules/Workspace/metadata/models/WorkspaceFolderContent.model.json';
import workspaceNavigation from '../../../../../modules/Workspace/metadata/navigation/Workspace.nav.json';
import tree from '../../../../../modules/Workspace/metadata/views/WorkspaceStructure.view.json';
import list from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderList.view.json';
import detail from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderDetail.view.json';
import partList from '../../../../../modules/Part/metadata/views/PartList.view.json';
import partDetail from '../../../../../modules/Part/metadata/views/PartDetail.view.json';
import documentList from '../../../../../modules/Document/metadata/views/DocumentList.view.json';
import documentDetail from '../../../../../modules/Document/metadata/views/DocumentDetail.view.json';
import partNavigation from '../../../../../modules/Part/metadata/navigation/Part.nav.json';
import documentNavigation from '../../../../../modules/Document/metadata/navigation/Document.nav.json';
import dictionary from '../../../../../modules/Workspace/metadata/dictionaries/Workspace.dic.json';
import partDictionary from '../../../../../modules/Part/metadata/dictionaries/Part.dic.json';
import documentDictionary from '../../../../../modules/Document/metadata/dictionaries/Document.dic.json';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { AuthProvider } from '../contexts/AuthContext';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { StandardCrudHostEnvironmentContext, type StandardCrudHostEnvironment } from '../standard-crud/standardCrudHostEnvironment';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => { await Promise.all([import('../pages/StandardCrudListDrawerRuntime'), import('@ant-design/pro-descriptions')]); });

it.each(['part', 'document'] as const)('opens same EID through the registered %s owning read-only page and target feature', async type => {
  const user = { id: 'actor', ecode: 'actor', ename: '账号', revision: 1, mustChangePassword: false, isLocalAdministrator: false,
    timeZoneId: 'Asia/Taipei', featurePositions: ['workspace-entry', 'workspace-add', 'part-entry', 'document-entry'] };
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const writes: unknown[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname === '/api/workspace/content/references') {
      writes.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
      return json({ succeeded: true });
    }
    if (url.pathname === '/api/workspace/folders/candidates') return json({ items: [], total: 0, page: 1, pageSize: 20 });
    if (url.pathname === `/api/${type}/${type === 'part' ? 'parts' : 'documents'}`) {
      expect(url.searchParams.get('fields')).toBe('EID,ECODE,ENAME');
      expect(new Headers(input instanceof Request ? input.headers : init?.headers).get('X-NewP-Feature')).toBe(type + '-entry');
      return json({ items: [{ EID: 'same-id', ECODE: 'SAME-CODE', ENAME: '无引用候选' }], total: 1, page: 1, pageSize: 20 });
    }
    if (url.pathname === '/api/links/workspace.content') return json({ items: ['part', 'document'].map(owner => ({ linkId: owner + '-relation',
      associatedObject: { modelKey: owner === 'part' ? 'Part/Part' : 'Document/Document', id: 'same-id' },
      fields: { LID: owner + '-relation', ObjectType: owner === 'part' ? 'Part/Part' : 'Document/Document',
        ObjectType_DisplayValue: owner === 'part' ? '零组件' : '图文档', Name: owner + '-name', Code: 'SAME-CODE' } })), total: 2, page: 1, pageSize: 20 });
    if (url.pathname === `/api/${type}/${type === 'part' ? 'parts' : 'documents'}/same-id`) {
      const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
      expect(headers.get('X-NewP-Feature')).toBe(type + '-entry');
      return json({ fields: { EID: 'same-id', ECODE: 'SAME-CODE', ENAME: type + '-owned-properties', Description: '所属对象说明',
        CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
        CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' }, summaries: {} });
    }
    throw new Error(`Unexpected read ${url.pathname}`);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace },
    { metadata: { moduleId: 'Part', displayName: '零组件', version: '1' }, ...part },
    { metadata: { moduleId: 'Document', displayName: '图文档', version: '1' }, ...documentModule }], 'workspace-mixed');
  const navs = [partNavigation, documentNavigation].map((navigation, index) => ({ ...navigation, navigation: { items: navigation.navigation.items.map(item => ({ ...item, featurePosition: index ? 'document-entry' : 'part-entry' })) } }));
  const views = [tree, list, detail, partList, partDetail, documentList, documentDetail];
  expect(compileStandardCrudRouteProjection(ModuleRegistry.getAllModules(), [workspaceNavigation, ...navs], views, []).detailEntries).toHaveLength(2);
  const catalogs = { zhs: Object.fromEntries([[dictionary.Workspace, 'Workspace'], [partDictionary.Part, 'Part'], [documentDictionary.Document, 'Document']].flatMap(([dict, key]) =>
    Object.entries(dict).map(([name, value]) => [`${key}.${name}`, (value as { zhs: string }).zhs]))) };
  const environment: StandardCrudHostEnvironment = { address: null, lifecycle: { active: true, reportState: () => undefined }, sync: { refreshVersion: 0 },
    input: { queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '[]', anchorId: 'root' } }, presentation: { kind: 'workspace', composed: true } };
  function OwnedPage() { const { id } = useParams(); return <FeaturePlacementContext.Provider value={{ entryPosition: type + '-entry' }}>
    <StandardCrudDetailPageHost viewKey={type === 'part' ? partDetail.view.key : documentDetail.view.key} id={id ?? null} registration={type === 'part' ? partResource : documentResource} />
  </FeaturePlacementContext.Provider>; }
  const wrap = (page: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="workspace-mixed">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
        { key: 'workspace', position: 'workspace-entry', kind: 'navigation' }, { key: 'add', position: 'workspace-add', kind: 'action', parentPosition: 'workspace-entry', staticScope: 'folder', actionScope: 'list.pageActions' },
      ] }, ...['Part', 'Document'].map(moduleId => ({ moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [{ key: moduleId === 'Part' ? 'part.parts' : 'document.documents', kind: 'navigation', position: moduleId.toLowerCase() + '-entry' }] }))] },
      navDocuments: navs, modelDocuments: [folder, content, partModel, documentModel], viewDocuments: views }}>
        {page}
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/workspace', element: wrap(<FeaturePlacementContext.Provider value={{ entryPosition: 'workspace-entry', staticScope: 'folder' }}><StandardCrudHostEnvironmentContext.Provider value={environment}><StandardCrudListPageHost viewKey={list.view.key} registration={resource} /></StandardCrudHostEnvironmentContext.Provider></FeaturePlacementContext.Provider>) },
    { path: type === 'part' ? '/parts/:id' : '/documents/:id', element: wrap(<OwnedPage />) }], { initialEntries: ['/workspace'] });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'workspace-mixed-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector(`tr[data-row-key="${type}-relation"]`)).not.toBeNull());
  await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '添加已有对象')!.click());
  await vi.waitFor(() => expect(document.querySelectorAll('.ant-modal .ant-select-selector')).toHaveLength(2));
  await act(async () => document.querySelectorAll<HTMLElement>('.ant-modal .ant-select-selector')[0].dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect([...document.querySelectorAll('.ant-select-item-option-content')].some(node => node.textContent === (type === 'part' ? '零组件' : '图文档'))).toBe(true));
  await act(async () => [...document.querySelectorAll<HTMLElement>('.ant-select-item-option-content')].find(node => node.textContent === (type === 'part' ? '零组件' : '图文档'))!.click());
  await act(async () => document.querySelectorAll<HTMLElement>('.ant-modal .ant-select-selector')[1].dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect([...document.querySelectorAll('.ant-select-item-option-content')].some(node => node.textContent === 'SAME-CODE - 无引用候选')).toBe(true));
  await act(async () => [...document.querySelectorAll<HTMLElement>('.ant-select-item-option-content')].find(node => node.textContent === 'SAME-CODE - 无引用候选')!.click());
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-footer .ant-btn-primary')!.click());
  await vi.waitFor(() => expect(writes).toEqual([{ position: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, target: { modelKey: type === 'part' ? 'Part/Part' : 'Document/Document', id: 'same-id' } }]));
  await vi.waitFor(() => expect(document.querySelector('.ant-modal')).toBeNull());
  const row = container.querySelector(`tr[data-row-key="${type}-relation"]`)!;
  await act(async () => [...row.querySelectorAll<HTMLElement>('*')].find(node => node.children.length === 0 && node.textContent === '更多')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(node => node.textContent === '打开属性')!.click());
  await vi.waitFor(() => expect(router.state.location.pathname).toBe(type === 'part' ? '/parts/same-id' : '/documents/same-id'));
  await vi.waitFor(() => expect(container.textContent).toContain(type + '-owned-properties'));
  expect(container.textContent).toContain('审计信息'); expect(container.textContent).toContain('更新账号');
  expect(container.querySelector('input,textarea')).toBeNull();
  expect(fetcher.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('-relation'))).toBe(false);
});
