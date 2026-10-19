import { toolbarMenuAction, toolbarActionDisabled } from './toolbarTestActions';
// @vitest-environment jsdom
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { componentRoot, loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { validateCompositionCandidate } from '../composition/compositionRegistry';
import { loadActiveModuleRuntimeWith } from '../active-module-runtime-loader';
import { buildPageEntriesFromNavDocuments } from '../navigation/pageEntryAdapter';
import { EntryNavigationProvider } from '../navigation/EntryNavigationContext';
import { PageEntry, PageOutlet } from '../composition/PageOutlet';
import { PageComposition } from '../composition/PageCompositionContext';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { AuthProvider } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';
import { ActionInstanceContext } from '../composition/pageCompositionRuntime';

vi.unmock('@icp-platform/core');
const owners = ['Workspace', 'Part', 'Document'];
const modules = Object.fromEntries(await Promise.all(owners.map(async owner => [owner,
  (await loadComponentSource<{ default: FrontendBehaviorBindings }>(owner, 'index.ts')).default])));
const fixture = (name: string) => JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', name + '.contract.json'), 'utf8'));
const notificationFeatures = fixture('WorkspaceTransferNotificationFeatures');
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const position = { spaceId: 'personal-space', rootFolderId: 'personal-root', linkIds: ['receiving-directory'] };
const location = { objectId: 'original-eid', modelKey: 'Part', sourceLinkId: 'received-lid', position };

async function mount(hasObjectRead: boolean, transferBatch = false) {
  const documents = (owner: string, folder: string) => {
    const path = resolve(componentRoot(owner), 'metadata', folder);
    return readdirSync(path).filter(file => file.endsWith('.json')).map(file => JSON.parse(readFileSync(resolve(path, file), 'utf8')));
  };
  const metadata: RuntimeMetadataState = { manifest: { modules: [] }, navDocuments: owners.flatMap(owner => documents(owner, 'navigation')),
    viewDocuments: owners.flatMap(owner => documents(owner, 'views')),
    modelDocuments: ['WorkspaceFolder', 'Part', 'Document'].map(model => fixture(model + 'Runtime'))
      .concat(readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json')) };
  const nav = metadata.navDocuments.flatMap(document => document.navigation.items);
  metadata.manifest.modules = owners.map(owner => {
    const history = fixture(owner + 'HistoryFeatures'); const transfer = fixture(owner + 'TransferFeatures');
    const readKey = { Workspace: 'workspace', Part: 'part.parts', Document: 'document.documents' }[owner]!;
    const historyKey = { Workspace: 'workspace.history', Part: 'part.history', Document: 'document.history' }[owner]!;
    nav.filter(item => item.featureKey === readKey).forEach(item => { item.featurePosition = history.readPosition; });
    nav.find(item => item.key === historyKey)!.featurePosition = history.historyPosition;
    const features = [{ key: readKey, position: history.readPosition, kind: 'navigation' },
      { key: historyKey, position: history.historyPosition, kind: 'navigation', parentPosition: history.readPosition },
      { key: 'transfer', position: transfer.transferPosition, kind: 'action', parentPosition: history.readPosition,
        staticScope: owner === 'Workspace' ? 'folder' : undefined, actionScope: 'list.rowActions' }];
    if (owner === 'Workspace') {
      nav.find(item => item.key === 'workspace.transfers')!.featurePosition = notificationFeatures.notificationsPosition;
      features.push({ key: 'transferSelected', position: transfer.batchPosition, kind: 'action', parentPosition: history.readPosition, staticScope: 'folder', actionScope: 'list.pageActions' });
      features.push({ key: 'workspace.transfers', position: notificationFeatures.notificationsPosition, kind: 'navigation', parentPosition: history.readPosition });
    }
    return { moduleId: owner, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features };
  });
  const entries = buildPageEntriesFromNavDocuments(metadata.navDocuments);
  const transferFeatures = fixture('WorkspaceTransferFeatures');
  const grants = [notificationFeatures.notificationsPosition, ...(hasObjectRead ? [notificationFeatures.readPosition] : []), ...(transferBatch ? [transferFeatures.batchPosition] : [])];
  const user = { id: 'recipient', ecode: 'recipient', ename: 'Recipient', revision: 1, mustChangePassword: false, featurePositions: grants, timeZoneId: 'UTC' };
  const requests: Request[] = [];
  let finishTransfer: (response: Response) => void = () => {};
  const transferCompletion = new Promise<Response>(resolve => { finishTransfer = resolve; });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request); const url = new URL(request.url);
    if (url.pathname.endsWith('/current-user')) return json(user);
    if (url.pathname.endsWith('/antiforgery-token')) return json({ requestToken: 'transfer-token' });
    if (url.pathname === '/api/links/workspace.content') return json({ items: ['a', 'b'].map(id => ({ linkId: 'link-' + id,
      associatedObject: { modelKey: 'Part', id: 'object-' + id }, fields: { LID: 'link-' + id, ObjectType: 'Part', Name: '零组件' + id, Code: id } })), total: 2, page: 1, pageSize: 20 });
    if (url.pathname === '/api/workspace/ownership/transfer/eligibility') return json({ allowed: true });
    if (url.pathname === '/api/workspace/ownership/transfer/accounts') return json([{ accountId: 'recipient', code: 'recipient', name: '接收人', departmentId: null }]);
    if (url.pathname === '/api/workspace/ownership/transfer' && request.method === 'POST') return transferCompletion;
    if (url.pathname === '/api/workspace/transfers/notifications') return json({ items: [
      { id: 'own-notification', occurredAt: '2026-10-18T00:00:00Z', senderName: '转交人', targetName: '原零组件', status: 'available', location },
      { id: 'hidden-notification', occurredAt: '2026-10-18T00:00:00Z', senderName: null, targetName: null, status: 'unavailable', location: null },
    ], total: 2, page: 1, pageSize: 20 });
    if (url.pathname === '/api/workspace/content/received-lid/properties') return json({ resourceKey: 'part.part', objectId: 'original-eid',
      detail: { fields: { EID: 'original-eid', ECODE: 'P-1', ENAME: '原零组件', Description: '', OwnerId: null, OwnerDepartmentId: null,
        CreatedBy: 'sender', CreatedAt: '2026-10-18T00:00:00Z', UpdatedBy: 'sender', UpdatedAt: '2026-10-18T00:00:00Z' }, summaries: {} } });
    throw new Error(`Unexpected notification request ${request.method} ${url.pathname}`);
  }));
  const capsules = owners.map(owner => readComponentJson(owner, 'newp.plugin.json'));
  const loaded = await loadActiveModuleRuntimeWith({ fetchCatalog: async () => ({ ok: true, status: 200, json: async () => ({ schemaVersion: '1.0', snapshotId: 'transfers', planHash: 'transfers',
    modules: capsules.map(capsule => ({ moduleId: capsule.moduleId, version: capsule.version, displayName: capsule.displayName, dependencies: capsule.dependencies,
      frontend: { moduleId: capsule.moduleId, behaviorKey: capsule.frontend.behaviorKey, bindingMode: 'behavior-bindings', bindings: capsule.frontend.bindings } })) }) }),
    internalModules: Object.fromEntries(owners.map(owner => [owner, async () => ({ default: modules[owner] })])) });
  expect(() => validateCompositionCandidate(loaded.modules, metadata)).not.toThrow();
  ModuleRegistry.replaceAll(loaded.modules, 'transfers');
  const words = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json').Workspace;
  const catalog = Object.fromEntries(Object.entries(words).map(([key, value]) => [`Workspace.${key}`, (value as { zhs: string }).zhs]));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const actionInstance = transferBatch ? new ActionInstanceScope() : undefined;
  const deactivate = actionInstance?.activate();
  const wrap = (content: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={grants}><CurrentPermissionScope><RuntimeMetadataProvider value={metadata}>
      <EntryNavigationProvider><ActionInstanceContext.Provider value={actionInstance}><PageComposition>{content}</PageComposition></ActionInstanceContext.Provider></EntryNavigationProvider>
    </RuntimeMetadataProvider></CurrentPermissionScope></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([
    ...entries.filter(entry => ['/workspace/transfers', '/workspace/transfers/object'].includes(entry.path))
      .map(entry => ({ path: entry.path, element: wrap(<PageEntry page={entry.page!} />) })),
    { path: '/workspace', element: wrap(<PageOutlet page={{ pageKey: 'platform.list', parameters: { viewKey: 'workspace.folder.list' }, inputResolverKey: 'workspace.folder.content' }}
      featurePlacement={{ entryPosition: notificationFeatures.readPosition, staticScope: 'folder' }} context={{ nodeKey: 'receiving-directory', nodeType: 'folder', label: 'Container', hasChildren: true,
        context: { id: 'container-object', ...position } }} />) },
  ], { initialEntries: [transferBatch ? '/workspace' : '/workspace/transfers'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); deactivate?.(); container.remove(); ModuleRegistry.replaceAll([], 'transfers-cleanup'); vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector(transferBatch ? 'tr[data-row-key="link-a"]' : 'tr[data-row-key="own-notification"]')).not.toBeNull());
  return { container, router, requests, finishTransfer };
}

it('opens the received original through the separately qualified Workspace read alias without mutation or dirty navigation', async () => {
  const { container, router, requests } = await mount(true);
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="own-notification"] button[aria-label="更多"]')!.click());
  const open = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent?.trim() === '打开属性');
  await vi.waitFor(() => expect(open()).toBeDefined()); act(() => open()!.click());
  await vi.waitFor(() => expect(router.state.location.pathname).toBe('/workspace/transfers/object'));
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/received-lid/properties'))).toBe(true));
  const properties = requests.find(request => new URL(request.url).pathname.endsWith('/received-lid/properties'))!;
  expect(properties.headers.get('X-NewP-Feature')).toBe(notificationFeatures.readPosition);
  expect(JSON.parse(new URL(properties.url).searchParams.get('queryScope')!)).toEqual(position);
  expect(router.state.location.state?.pageParameters.objectId).toBe('original-eid');
  expect(requests.find(request => new URL(request.url).pathname.endsWith('/notifications'))!.headers.get('X-NewP-Feature')).toBe(notificationFeatures.notificationsPosition);
  expect(document.body.textContent).not.toContain('放弃未保存修改');
  expect(requests.every(request => request.method === 'GET')).toBe(true);
  expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/part/'))).toBe(false);
});

it('does not offer an original-object open to notification-only readers or unavailable rows', async () => {
  const { container, requests } = await mount(false);
  const row = container.querySelector('tr[data-row-key="own-notification"]')!;
  expect(row.querySelector('button[aria-label="更多"]')).toBeNull();
  expect(container.querySelector('tr[data-row-key="hidden-notification"] button[aria-label="更多"]')).toBeNull();
  expect(container.textContent).toContain('对象不可用');
  expect(requests.some(request => new URL(request.url).pathname.endsWith('/properties'))).toBe(false);
});

it('submits the actual Workspace ListHost selected rows as one fixed full-source batch', async () => {
  const { container, requests, finishTransfer } = await mount(true, true);
  for (const id of ['link-a', 'link-b']) {
    await vi.waitFor(() => expect(container.querySelector(`tr[data-row-key="${id}"] input[type="checkbox"]`)).not.toBeNull());
    await act(async () => container.querySelector<HTMLInputElement>(`tr[data-row-key="${id}"] input[type="checkbox"]`)!.click());
  }
  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === label);
  await vi.waitFor(async () => expect(toolbarActionDisabled(await toolbarMenuAction(container, '授权与归属', '转交选中对象'))).toBe(false));
  const transferAction = await toolbarMenuAction(container, '授权与归属', '转交选中对象');
  await act(async () => transferAction!.click());
  const accounts = () => requests.filter(request => new URL(request.url).pathname.endsWith('/transfer/accounts'));
  const select = () => document.querySelector('[role="dialog"] .ant-select');
  await vi.waitFor(async () => { await act(async () => undefined); expect(accounts()).toHaveLength(1); expect(select()?.classList.contains('ant-select-disabled')).toBe(false); });
  const cancel = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button')].find(item => item.textContent?.replace(/\s/g, '') === '取消');
  expect(cancel).toBeDefined(); await act(async () => cancel!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  expect(requests.some(request => request.method === 'POST')).toBe(false);
  const reopenedTransferAction = await toolbarMenuAction(container, '授权与归属', '转交选中对象');
  await act(async () => reopenedTransferAction!.click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(accounts()).toHaveLength(2); expect(select()?.classList.contains('ant-select-disabled')).toBe(false); });
  await act(async () => document.querySelector('[role="dialog"] [role="combobox"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.querySelector('[title="接收人 (recipient)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="接收人 (recipient)"]')!.click());
  await vi.waitFor(() => expect(button('确认')?.disabled).toBe(false));
  await act(async () => button('确认')!.click());
  await vi.waitFor(() => expect(button('确认')?.disabled).toBe(true));
  expect(document.querySelector('[role="dialog"] .ant-select-disabled')).not.toBeNull();
  expect([...container.querySelectorAll<HTMLInputElement>('tr[data-row-key] input[type="checkbox"]')].every(input => input.disabled)).toBe(true);
  await act(async () => button('确认')!.click());
  expect(requests.filter(request => request.method === 'POST')).toHaveLength(1);
  await act(async () => finishTransfer(json({ succeeded: true, changed: 2 })));
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  await vi.waitFor(async () => { await act(async () => undefined); expect(requests.filter(request => new URL(request.url).pathname === '/api/links/workspace.content').length).toBeGreaterThan(1); });
  const write = requests.filter(request => request.method === 'POST' && new URL(request.url).pathname === '/api/workspace/ownership/transfer');
  expect(write).toHaveLength(1);
  expect(write[0].headers.get('X-NewP-Feature')).toBe(fixture('WorkspaceTransferFeatures').batchPosition);
  expect(await write[0].clone().json()).toEqual({ position, linkIds: ['link-a', 'link-b'], recipientId: 'recipient' });
  for (const request of requests.filter(request => new URL(request.url).pathname.endsWith('/transfer/eligibility'))) {
    expect(JSON.parse(new URL(request.url).searchParams.get('selectedIds')!)).toEqual(['link-a', 'link-b']);
  }
});
