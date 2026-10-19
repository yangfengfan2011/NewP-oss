// @vitest-environment jsdom
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import type { StandardCrudActionDefinition } from '@icp-platform/sdk';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';
import workspace from '../../../../modules/infor-center/workspace/src';
import { partBindings } from '../../../../modules/infor-center/part/src';
import { documentBindings } from '../../../../modules/infor-center/document/src';
import { validateCompositionCandidate } from '../composition/compositionRegistry';
import { resolveDetailContributions } from '../standard-crud/detailContributionRegistry';
import { loadActiveModuleRuntimeWith } from '../active-module-runtime-loader';
import { EntryNavigationProvider } from '../navigation/EntryNavigationContext';
import { PageEntry, PageOutlet } from '../composition/PageOutlet';
import { PageComposition } from '../composition/PageCompositionContext';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { AuthProvider } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
await import('../composition/StandardTabPage');
await import('../pages/StandardCrudDetailPageHost');
await import('../pages/StandardCrudListPageHost');
await import('../../../../modules/infor-center/part/src/partResource');
await import('../../../ui/src/ObjectAuthorizationInput');

const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['container'] };
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
function authoring(): RuntimeMetadataState {
  const documents = (owner: string, folder: string) => {
    const root = resolve(__dirname, '../../../../modules/infor-center', owner, 'metadata', folder);
    return readdirSync(root).filter(file => file.endsWith('.json')).map(file => JSON.parse(readFileSync(resolve(root, file), 'utf8')));
  };
  return { manifest: { modules: [] }, navDocuments: ['workspace', 'part', 'document'].flatMap(owner => documents(owner, 'navigation')),
    viewDocuments: ['workspace', 'part', 'document'].flatMap(owner => documents(owner, 'views')),
    modelDocuments: ['WorkspaceFolder', 'Part', 'Document'].map(model => JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', model + 'Runtime.contract.json'), 'utf8'))) };
}
it.each(['Part', 'WorkspaceFolder', 'Document'] as const)('saves a Workspace %s authorization through its real standard action with EID as target and LID as source', async modelKey => {
  const sourceLinkId = modelKey === 'Part' ? 'part-occurrence' : `${modelKey.toLowerCase()}-occurrence`;
  const objectId = modelKey === 'Part' ? 'part-object' : `${modelKey.toLowerCase()}-object`;
  const resourceKey = { Part: 'part.part', WorkspaceFolder: 'workspace.folder', Document: 'document.document' }[modelKey];
  const sourcePath = `/api/workspace/content/${sourceLinkId}/permissions`;
  const positions = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures/WorkspacePermissionFeatures.contract.json'), 'utf8')) as { readPosition: string; managePosition: string };
  const metadata = authoring();
  const list = metadata.viewDocuments.find(document => document.view.key === 'workspace.folder.list')!.view;
  const definition = (list.rowActions as StandardCrudActionDefinition[]).find(action => action.key === 'authorizeContent');
  expect(definition).toBeDefined();
  const binding = workspace.standardCrudCapabilities!.find(item => item.targetKey === 'workspace.folder.list')!;
  const { default: registration } = await binding.load();
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (!url.pathname.startsWith(sourcePath)) throw new Error(`Unexpected management request ${url.pathname}`);
    if (url.pathname.endsWith('/eligibility')) return json({ allowed: true });
    if (url.pathname.endsWith('/catalog')) return json({ actions: [{ key: 'read', title: '读取', enabled: true }],
      subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }] });
    if (request.method === 'PUT') return new Response(null, { status: 204 });
    return json({ resourceKey, objectId, subjects: [] });
  }));
  const instance = new ActionInstanceScope();
  const deactivate = instance.activate();
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
      <AuthProvider initialUser={{ id: 'manager', ecode: 'manager', ename: 'Manager', revision: 1, mustChangePassword: false, featurePositions: [positions.managePosition] }}>
        <CurrentPermissionScope><StandardCrudActions actions={[definition!]} handlers={registration.actionHandlers}
          actionInputs={registration.actionInputRenderers} actionEligibilityResolvers={registration.actionEligibilityResolvers}
          featureResolutionForAction={() => ({ kind: 'allowed', position: positions.managePosition })} refresh={vi.fn()}
          context={{ id: sourceLinkId, row: { LID: sourceLinkId, ObjectModelKey: modelKey, ObjectId: objectId, Name: `Source ${modelKey}` },
            queryScope: { spaceId: position.spaceId, rootFolderId: position.rootFolderId, linkIds: JSON.stringify(position.linkIds) }, instance }} />
        </CurrentPermissionScope>
      </AuthProvider>
    </LocalizationProvider>,
  }], { initialEntries: ['/workspace'] });
  onTestFinished(async () => {
    await act(async () => root.unmount()); deactivate(); router.dispose(); container.remove(); vi.unstubAllGlobals();
  });
  await act(async () => root.render(<RouterProvider router={router} />));
  const manageButton = () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === definition!.labelKey);
  await vi.waitFor(() => expect(manageButton()).toBeDefined());
  await act(async () => manageButton()!.click());
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  const writes = requests.filter(request => request.method === 'PUT');
  expect(writes).toHaveLength(1);
  expect(await writes[0].clone().json()).toEqual({ resourceKey, objectId, subjects: [] });
  for (const request of requests.filter(request => new URL(request.url).pathname.includes('/permissions'))) {
    expect(request.headers.get('X-NewP-Feature')).toBe(positions.managePosition);
    expect(JSON.parse(new URL(request.url).searchParams.get('queryScope')!)).toEqual(position);
  }
  expect(document.querySelector('[role="dialog"]')).toBeNull();
});

it.each([
  ...(['valid', 'featurePosition', 'sourceFeaturePosition', 'missingSource'] as const).map(variant => ({ variant, modelKey: 'Part', resourceKey: 'part.part' })),
  { variant: 'valid' as const, modelKey: 'WorkspaceFolder', resourceKey: 'workspace.folder' },
  { variant: 'valid' as const, modelKey: 'Document', resourceKey: 'document.document' },
])('opens Workspace to $modelKey from its real row with only the compiled browse grant: $variant', async ({ variant, modelKey, resourceKey }) => {
  const sourceLinkId = modelKey === 'Part' ? 'part-occurrence' : `${modelKey.toLowerCase()}-occurrence`;
  const objectId = modelKey === 'Part' ? 'part-object' : `${modelKey.toLowerCase()}-object`;
  const sourceName = `Source ${modelKey}`;
  const sourcePath = `/api/workspace/content/${sourceLinkId}`;
  const positions = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures/WorkspacePermissionFeatures.contract.json'), 'utf8')) as { readPosition: string; managePosition: string };
  const metadata = authoring();
  metadata.modelDocuments.push(JSON.parse(readFileSync(resolve(__dirname, '../../../../modules/infor-center/workspace/metadata/models/WorkspaceFolderContent.model.json'), 'utf8')));
  const nav = metadata.navDocuments.flatMap(document => document.navigation.items);
  metadata.manifest.modules = ['Workspace', 'Part', 'Document'].map(moduleId => {
    const compiled = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${moduleId}PermissionFeatures.contract.json`), 'utf8')) as { readPosition: string; managePosition: string };
    const history = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${moduleId}HistoryFeatures.contract.json`), 'utf8')) as { readPosition: string; historyPosition: string };
    expect(history.readPosition).toBe(compiled.readPosition);
    const navigationKey = { Workspace: 'workspace', Part: 'part.parts', Document: 'document.documents' }[moduleId]!;
    const historyKey = { Workspace: 'workspace.history', Part: 'part.history', Document: 'document.history' }[moduleId]!;
    nav.find(item => item.key === navigationKey)!.featurePosition = compiled.readPosition;
    nav.find(item => item.key === historyKey)!.featurePosition = history.historyPosition;
    return { moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
      { key: navigationKey, position: compiled.readPosition, kind: 'navigation' as const },
      { key: historyKey, position: history.historyPosition, kind: 'navigation' as const, parentPosition: compiled.readPosition },
      { key: moduleId === 'Workspace' ? 'authorizeContent' : 'authorize', position: compiled.managePosition, kind: 'action' as const,
        parentPosition: compiled.readPosition, actionScope: 'list.rowActions', ...(moduleId === 'Workspace' ? { staticScope: 'folder' } : {}) },
    ] };
  });
  const targetNavigation = metadata.navDocuments.flatMap(document => document.navigation.items).find(item => item.key === 'workspace.permissions')!;
  expect(targetNavigation.featureAuthorization).toBe(false);
  expect(targetNavigation.featureKey).toBeUndefined(); expect(targetNavigation.featureOwner).toBeUndefined();
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('/current-user')) return json({ id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false, featurePositions: [positions.readPosition] });
    if (url.pathname === '/api/links/workspace.content') return json({ items: [{ linkId: sourceLinkId, associatedObject: { modelKey, id: objectId },
      fields: { LID: sourceLinkId, ObjectType: modelKey, Name: sourceName, Code: 'P-1' } }], total: 1, page: 1, pageSize: 20 });
    if (url.pathname === `${sourcePath}/properties`) return json({ resourceKey, objectId,
      detail: { fields: { EID: objectId, ECODE: 'P-1', ENAME: sourceName, Description: '', OwnerId: null, OwnerDepartmentId: null,
        CreatedBy: 'reader', CreatedAt: '2026-10-18T00:00:00Z', UpdatedBy: 'reader', UpdatedAt: '2026-10-18T00:00:00Z' }, summaries: {} } });
    if (url.pathname.endsWith('/catalog')) return json({ actions: [{ key: 'read', title: '读取', enabled: true }], subjectTypes: [] });
    if (url.pathname.endsWith('/subjects/user')) return json({ items: [], currentValues: [], hasMore: false });
    if (url.pathname.endsWith('/explanation')) return json({ resourceKey, objectId, accountId: 'reader', accountName: 'Reader', actions: [], subjectNames: [] });
    throw new Error(`Unexpected source request: ${request.method} ${url.pathname}`);
  }));
  const capsules = ['workspace', 'part', 'document'].map(owner => JSON.parse(readFileSync(resolve(__dirname, '../../../../modules/infor-center', owner, 'newp.plugin.json'), 'utf8')));
  const loaded = await loadActiveModuleRuntimeWith({
    fetchCatalog: async () => ({ ok: true, status: 200, json: async () => ({ schemaVersion: '1.0', snapshotId: 'source-tab', planHash: 'source-tab',
      modules: capsules.map(capsule => ({ moduleId: capsule.moduleId, version: capsule.version, displayName: capsule.displayName, dependencies: capsule.dependencies,
        frontend: { moduleId: capsule.moduleId, behaviorKey: capsule.frontend.behaviorKey, bindingMode: 'behavior-bindings', bindings: capsule.frontend.bindings } })) }) }),
    internalModules: { Workspace: async () => ({ default: workspace }), Part: async () => ({ default: partBindings }), Document: async () => ({ default: documentBindings }) },
  });
  expect(() => validateCompositionCandidate(loaded.modules, metadata)).not.toThrow();
  expect(resolveDetailContributions(loaded.modules, metadata).filter(entry => entry.navigationKey === 'workspace').map(entry => entry.key)).toEqual([
    'workspace.folder.references', 'workspace.part.references', 'workspace.document.references',
  ]);
  // 只替换外部定位投影这一接缝，真实模块的读取、View、资格与调度都继续消费正式实现。
  if (variant !== 'valid') loaded.modules = loaded.modules.map(module => module.metadata.moduleId !== 'Workspace' ? module : ({ ...module,
    standardCrudCapabilities: module.standardCrudCapabilities?.map(binding => binding.targetKey !== 'workspace.folder.list' ? binding : ({ ...binding,
      load: async () => {
        const resource = (await binding.load()).default;
        return { default: { ...resource, dataAdapter: { ...resource.dataAdapter, getReadNavigationParameters: (row, context) => {
          const parameters = { ...resource.dataAdapter.getReadNavigationParameters!(row, context) };
          if (variant === 'missingSource') delete parameters.sourceLinkId;
          else parameters[variant] = 'forged-feature';
          return parameters;
        } } } };
      },
    })),
  }));
  ModuleRegistry.replaceAll(loaded.modules, 'workspace-part-permissions');
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const wrap = (content: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    <AuthProvider initialUser={{ id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false, featurePositions: [positions.readPosition] }}>
      <AuthorizationProvider featurePositions={[positions.readPosition]}><CurrentPermissionScope><RuntimeMetadataProvider value={metadata}>
        <EntryNavigationProvider>{content}</EntryNavigationProvider>
      </RuntimeMetadataProvider></CurrentPermissionScope></AuthorizationProvider>
    </AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([
    { path: '/workspace', element: wrap(<PageComposition><PageOutlet page={{ pageKey: 'platform.list', parameters: { viewKey: 'workspace.folder.list' }, inputResolverKey: 'workspace.folder.content' }}
      featurePlacement={{ entryPosition: positions.readPosition, staticScope: 'folder' }} context={{ nodeKey: 'container', nodeType: 'folder', label: 'Container', hasChildren: true,
        context: { id: 'container-object', ...position } }} /></PageComposition>) },
    { path: targetNavigation.path!, element: wrap(<PageEntry page={targetNavigation.page!} />) },
  ], { initialEntries: ['/workspace'] });
  onTestFinished(async () => {
    await act(async () => root.unmount()); router.dispose(); container.remove();
    ModuleRegistry.replaceAll([], 'workspace-part-permissions-cleanup'); vi.unstubAllGlobals();
  });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector(`tr[data-row-key="${sourceLinkId}"]`)).not.toBeNull());
  const more = container.querySelector<HTMLButtonElement>(`tr[data-row-key="${sourceLinkId}"] button[aria-label="更多"]`);
  expect(more).not.toBeNull();
  await act(async () => more!.click());
  const permissionAction = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent?.trim() === 'Permissions');
  await vi.waitFor(() => expect(permissionAction(), 'A browse-only user must retain the readonly source viewer.').toBeDefined());
  expect([...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].some(item => item.textContent?.trim() === 'Authorize')).toBe(false);
  act(() => permissionAction()!.click());
  if (variant !== 'valid') {
    if (variant === 'missingSource') await vi.waitFor(() => expect(container.textContent).toContain('页面配置或上下文无效'));
    else expect(router.state.location.pathname).toBe('/workspace');
    expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/workspace/content/'))).toBe(false);
    expect(requests.some(request => new URL(request.url).pathname.match(/^\/api\/(part\/parts|document\/documents|workspace\/folders)\//) !== null)).toBe(false);
    return;
  }
  await vi.waitFor(() => {
    const phantomDiscard = [...document.querySelectorAll('[role="dialog"]')].find(dialog => dialog.textContent?.includes('放弃未保存的修改'));
    expect(phantomDiscard, 'Readonly navigation from a pristine list must not create an unsaved-draft confirmation.').toBeUndefined();
    expect(router.state.location.pathname).toBe(targetNavigation.path);
  });
  await vi.waitFor(() => expect(requests.map(request => new URL(request.url).pathname)).toContain(`${sourcePath}/properties`));
  expect(router.state.location.state.pageParameters.sourceFeaturePosition).toBe(positions.readPosition);
  expect(router.state.location.state.pageParameters.featurePosition).toBeUndefined();
  const permissionTab = [...container.querySelectorAll<HTMLElement>('[role="tab"]')].find(tab => tab.textContent?.includes('Permissions'));
  expect(permissionTab).toBeDefined();
  await act(async () => permissionTab!.click());
  await vi.waitFor(() => expect(container.textContent).toContain('Reader'));
  const sourceReads = requests.filter(request => new URL(request.url).pathname.startsWith(`${sourcePath}/`));
  expect(sourceReads.map(request => new URL(request.url).pathname)).toContain(`${sourcePath}/permissions/explanation`);
  for (const request of sourceReads) {
    expect(JSON.parse(new URL(request.url).searchParams.get('queryScope')!)).toEqual(position);
    expect(request.headers.get('X-NewP-Feature')).toBe(positions.readPosition);
  }
  expect(requests.some(request => new URL(request.url).pathname.match(/^\/api\/(part\/parts|document\/documents|workspace\/folders)\//) !== null)).toBe(false);
  expect(requests.some(request => new URL(request.url).pathname.endsWith('/permissions/eligibility'))).toBe(false);
  expect(requests.every(request => request.method === 'GET' && request.body === null)).toBe(true);
});

it.each(['part', 'document'] as const)('opens the independent %s permissions tabs with only its compiled browse grant', async owner => {
  const modelKey = owner === 'part' ? 'Part' : 'Document';
  const resourceKey = `${owner}.${owner}`;
  const api = owner === 'part' ? '/api/part/parts' : '/api/document/documents';
  const bindings = owner === 'part' ? partBindings : documentBindings;
  const positions = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${modelKey}PermissionFeatures.contract.json`), 'utf8')) as { readPosition: string; managePosition: string };
  const metadata = authoring();
  const nav = metadata.navDocuments.flatMap(document => document.navigation.items);
  metadata.manifest.modules = ['Workspace', 'Part', 'Document'].map(moduleId => {
    const compiled = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${moduleId}PermissionFeatures.contract.json`), 'utf8')) as { readPosition: string; managePosition: string };
    const navigationKey = { Workspace: 'workspace', Part: 'part.parts', Document: 'document.documents' }[moduleId]!;
    const history = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${moduleId}HistoryFeatures.contract.json`), 'utf8')) as { readPosition: string; historyPosition: string };
    expect(history.readPosition).toBe(compiled.readPosition);
    nav.find(item => item.key === navigationKey)!.featurePosition = compiled.readPosition;
    const historyKey = { Workspace: 'workspace.history', Part: 'part.history', Document: 'document.history' }[moduleId]!;
    nav.find(item => item.key === historyKey)!.featurePosition = history.historyPosition;
    return { moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
      { key: navigationKey, position: compiled.readPosition, kind: 'navigation' as const },
      { key: historyKey, position: history.historyPosition, kind: 'navigation' as const, parentPosition: compiled.readPosition },
      { key: moduleId === 'Workspace' ? 'authorizeContent' : 'authorize', position: compiled.managePosition, kind: 'action' as const,
        parentPosition: compiled.readPosition, actionScope: 'list.rowActions', ...(moduleId === 'Workspace' ? { staticScope: 'folder' } : {}) },
    ] };
  });
  const target = nav.find(item => item.key === `${resourceKey}.permissions`)!;
  expect(target.routeOnly).toBe(true); expect(target.featureAuthorization).toBe(false);
  expect(target.featureKey).toBeUndefined(); expect(target.featureOwner).toBeUndefined();
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('/current-user')) return json({ id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false, featurePositions: [positions.readPosition] });
    if (url.pathname === api) return json({ items: [{ EID: 'entity', ECODE: 'E-1', ENAME: 'Independent object' }], total: 1, page: 1, pageSize: 20 });
    if (url.pathname === `${api}/entity`) return json({ fields: { EID: 'entity', ECODE: 'E-1', ENAME: 'Independent object', Description: '',
      OwnerId: null, OwnerDepartmentId: null, CreatedBy: 'reader', CreatedAt: '2026-10-18T00:00:00Z', UpdatedBy: 'reader', UpdatedAt: '2026-10-18T00:00:00Z' }, summaries: {} });
    if (url.pathname === `${api}/entity/permissions/catalog`) return json({ actions: [{ key: 'read', title: '读取', enabled: true }], subjectTypes: [] });
    if (url.pathname === `${api}/entity/permissions/subjects/user`) return json({ items: [], currentValues: [], hasMore: false });
    if (url.pathname === `${api}/entity/permissions/explanation`) return json({ resourceKey, objectId: 'entity', accountId: 'reader', accountName: 'Reader', actions: [], subjectNames: [] });
    throw new Error(`Unexpected independent request: ${request.method} ${url.pathname}`);
  }));
  const capsules = ['workspace', 'part', 'document'].map(owner => JSON.parse(readFileSync(resolve(__dirname, '../../../../modules/infor-center', owner, 'newp.plugin.json'), 'utf8')));
  const loaded = await loadActiveModuleRuntimeWith({
    fetchCatalog: async () => ({ ok: true, status: 200, json: async () => ({ schemaVersion: '1.0', snapshotId: 'independent-tab', planHash: 'independent-tab',
      modules: capsules.map(capsule => ({ moduleId: capsule.moduleId, version: capsule.version, displayName: capsule.displayName, dependencies: capsule.dependencies,
        frontend: { moduleId: capsule.moduleId, behaviorKey: capsule.frontend.behaviorKey, bindingMode: 'behavior-bindings', bindings: capsule.frontend.bindings } })) }) }),
    internalModules: { Workspace: async () => ({ default: workspace }), Part: async () => ({ default: partBindings }), Document: async () => ({ default: documentBindings }) },
  });
  expect(() => validateCompositionCandidate(loaded.modules, metadata)).not.toThrow();
  ModuleRegistry.replaceAll(loaded.modules, 'independent-object-permissions');
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const wrap = (content: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    <AuthProvider initialUser={{ id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false, featurePositions: [positions.readPosition] }}>
      <AuthorizationProvider featurePositions={[positions.readPosition]}><CurrentPermissionScope><RuntimeMetadataProvider value={metadata}>
        <EntryNavigationProvider>{content}</EntryNavigationProvider>
      </RuntimeMetadataProvider></CurrentPermissionScope></AuthorizationProvider>
    </AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([
    { path: `/${owner}s`, element: wrap(<PageComposition><PageOutlet page={{ pageKey: 'platform.list', parameters: { viewKey: `${resourceKey}.list` } }}
      featurePlacement={{ entryPosition: positions.readPosition }} /></PageComposition>) },
    { path: target.path!, element: wrap(<PageEntry page={target.page!} />) },
  ], { initialEntries: [`/${owner}s`] });
  onTestFinished(async () => {
    await act(async () => root.unmount()); router.dispose(); container.remove();
    ModuleRegistry.replaceAll([], 'independent-object-permissions-cleanup'); vi.unstubAllGlobals();
  });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="entity"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="entity"] button[aria-label="更多"]')!.click());
  const permissionAction = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent?.trim() === 'Permissions');
  await vi.waitFor(() => expect(permissionAction()).toBeDefined());
  expect([...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].some(item => item.textContent?.trim() === 'Authorize')).toBe(false);
  act(() => permissionAction()!.click());
  await vi.waitFor(() => expect(container.querySelector('.np-crud-detail')?.textContent).toContain('Independent object'));
  expect(router.state.location.pathname).toBe(target.path);
  const permissionTab = [...container.querySelectorAll<HTMLElement>('[role="tab"]')].find(tab => tab.textContent?.includes('Permissions'));
  expect(permissionTab).toBeDefined();
  await act(async () => permissionTab!.click());
  await vi.waitFor(() => expect(container.textContent).toContain('Reader'));
  const ownerRequests = requests.filter(request => new URL(request.url).pathname.startsWith(api));
  expect(ownerRequests.map(request => new URL(request.url).pathname)).toContain(`${api}/entity/permissions/explanation`);
  expect(ownerRequests.every(request => request.headers.get('X-NewP-Feature') === positions.readPosition)).toBe(true);
  expect(requests.every(request => request.method === 'GET' && request.body === null)).toBe(true);
  expect(requests.some(request => new URL(request.url).pathname.includes('/workspace/content/') || new URL(request.url).pathname.endsWith('/permissions/eligibility'))).toBe(false);
  expect(bindings.pageInputResolvers!.find(binding => binding.key === `${resourceKey}.record`)!.resolve.bind(null, { recordId: 'entity' })).toThrow('缺少对象及来源读取位置');
});

it.each(['part', 'document'] as const)('saves independent %s authorization through its real standard action', async owner => {
  const modelKey = owner === 'part' ? 'Part' : 'Document';
  const resourceKey = `${owner}.${owner}`;
  const api = owner === 'part' ? '/api/part/parts' : '/api/document/documents';
  const positions = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${modelKey}PermissionFeatures.contract.json`), 'utf8')) as { readPosition: string; managePosition: string };
  const definition = (authoring().viewDocuments.find(document => document.view.key === `${resourceKey}.list`)!.view.rowActions as StandardCrudActionDefinition[]).find(action => action.key === 'authorize')!;
  const bindings = owner === 'part' ? partBindings : documentBindings;
  const registration = (await bindings.standardCrudCapabilities!.find(binding => binding.targetKey === `${resourceKey}.list`)!.load()).default;
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (!url.pathname.startsWith(`${api}/entity/permissions`)) throw new Error(`Unexpected independent save request: ${request.method} ${url.pathname}`);
    if (url.pathname.endsWith('/eligibility')) return json({ allowed: true });
    if (url.pathname.endsWith('/catalog')) return json({ actions: [{ key: 'read', title: '读取', enabled: true }], subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }] });
    if (request.method === 'PUT') return new Response(null, { status: 204 });
    return json({ resourceKey, objectId: 'entity', subjects: [] });
  }));
  const instance = new ActionInstanceScope();
  const deactivate = instance.activate();
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{ path: `/${owner}s`, element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
      <AuthProvider initialUser={{ id: 'manager', ecode: 'manager', ename: 'Manager', revision: 1, mustChangePassword: false, featurePositions: [positions.managePosition] }}>
        <CurrentPermissionScope><StandardCrudActions actions={[definition]} handlers={registration.actionHandlers}
          actionInputs={registration.actionInputRenderers} actionEligibilityResolvers={registration.actionEligibilityResolvers}
          featureResolutionForAction={() => ({ kind: 'allowed', position: positions.managePosition })} refresh={vi.fn()}
          context={{ id: 'entity', row: { EID: 'entity', ENAME: 'Independent object' }, instance }} />
        </CurrentPermissionScope>
      </AuthProvider>
    </LocalizationProvider>,
  }], { initialEntries: [`/${owner}s`] });
  onTestFinished(async () => {
    await act(async () => root.unmount()); deactivate(); router.dispose(); container.remove(); vi.unstubAllGlobals();
  });
  await act(async () => root.render(<RouterProvider router={router} />));
  const manageButton = () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === definition.labelKey);
  await vi.waitFor(() => expect(manageButton()).toBeDefined());
  await act(async () => manageButton()!.click());
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  const writes = requests.filter(request => request.method === 'PUT');
  expect(writes).toHaveLength(1);
  expect(await writes[0].clone().json()).toEqual({ resourceKey, objectId: 'entity', subjects: [] });
  expect(requests.filter(request => new URL(request.url).pathname.includes('/permissions')).every(request => request.headers.get('X-NewP-Feature') === positions.managePosition)).toBe(true);
  expect(requests.some(request => new URL(request.url).searchParams.has('queryScope'))).toBe(false);
  expect(document.querySelector('[role="dialog"]')).toBeNull();
});
