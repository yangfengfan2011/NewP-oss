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
import { renderPageEntry } from '../navigation/pageRouteRenderer';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { platformTestCatalogs } from './localizationTestCatalogs';
import { PageComposition } from '../composition/PageCompositionContext';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { AuthProvider } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';

vi.unmock('@icp-platform/core');
const owners = ['Workspace', 'Part', 'Document'];
const modules = Object.fromEntries(await Promise.all(owners.map(async owner => [owner,
  (await loadComponentSource<{ default: FrontendBehaviorBindings }>(owner, 'index.ts')).default])));
const fixture = (name: string) => JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', name + '.contract.json'), 'utf8'));
const notificationFeatures = fixture('WorkspaceTransferNotificationFeatures');
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const position = { spaceId: 'personal-space', rootFolderId: 'personal-root', linkIds: ['receiving-directory'] };
const location = { objectId: 'original-eid', modelKey: 'Part', sourceLinkId: 'received-lid', position };

async function mount(authorize = false, actorId = 'recipient') {
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
    nav.filter(item => item.featureKey === readKey || item.key === readKey).forEach(item => { item.featurePosition = history.readPosition; });
    nav.find(item => item.key === historyKey)!.featurePosition = history.historyPosition;
    const features = [{ key: readKey, position: history.readPosition, kind: 'navigation' },
      { key: historyKey, position: history.historyPosition, kind: 'navigation', parentPosition: history.readPosition },
      { key: 'transfer', position: transfer.transferPosition, kind: 'action', parentPosition: history.readPosition,
        staticScope: owner === 'Workspace' ? 'folder' : undefined, actionScope: 'list.rowActions' }];
    if (owner === 'Part') {
      features.push({ key: 'authorize', position: fixture('PartPermissionFeatures').managePosition, kind: 'action', parentPosition: history.readPosition, staticScope: undefined, actionScope: 'list.rowActions' });
    }
    if (owner === 'Workspace') {
      nav.find(item => item.key === 'workspace.transfers')!.featurePosition = notificationFeatures.notificationsPosition;
      features.push({ key: 'transferSelected', position: transfer.batchPosition, kind: 'action', parentPosition: history.readPosition, staticScope: 'folder', actionScope: 'list.pageActions' });
      features.push({ key: 'workspace.transfers', position: notificationFeatures.notificationsPosition, kind: 'navigation', parentPosition: history.readPosition });
    }
    return { moduleId: owner, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features };
  });
  const entries = buildPageEntriesFromNavDocuments(metadata.navDocuments);
  const transferFeatures = fixture('PartTransferFeatures');
  const grants = [transferFeatures.readPosition, authorize ? fixture('PartPermissionFeatures').managePosition : transferFeatures.transferPosition];
  const user = { id: actorId, ecode: actorId, ename: actorId, revision: 1, mustChangePassword: false, featurePositions: grants, timeZoneId: 'UTC' };
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request); const url = new URL(request.url);
    if (url.pathname.endsWith('/current-user')) return json(user);
    if (url.pathname.endsWith('/antiforgery-token')) return json({ requestToken: 'transfer-token' });
    if (url.pathname === '/api/part/parts') return json({ items: [{ EID: 'original-eid', ECODE: 'P-1', ENAME: '原零组件' }], total: 1, page: 1, pageSize: 20 });
    if (url.pathname === '/api/part/parts/original-eid/transfer/eligibility') return json({ allowed: true });
    if (url.pathname === '/api/part/parts/original-eid/transfer/accounts') return json([{ accountId: 'recipient', code: 'recipient', name: '接收人', departmentId: null }]);
    if (url.pathname === '/api/part/parts/original-eid/transfer' && request.method === 'POST') return json({ succeeded: true, changed: 1 });
    if (url.pathname.endsWith('/permissions/eligibility')) return json({ allowed: true });
    if (url.pathname.endsWith('/permissions/catalog')) return json({ actions: [{ key: 'read', title: '读取', enabled: true }], subjectTypes: [{ key: 'all', title: '所有人', requiresId: false }] });
    if (url.pathname === '/api/part/parts/original-eid/permissions') return request.method === 'PUT' ? new Response(null, { status: 204 }) : json({ resourceKey: 'part.part', objectId: 'original-eid', subjects: [] });
    if (url.pathname === '/api/links/workspace.content') return json({ items: ['a', 'b'].map(id => ({ linkId: 'link-' + id,
      associatedObject: { modelKey: 'Part', id: 'object-' + id }, fields: { LID: 'link-' + id, ObjectType: 'Part', Name: '零组件' + id, Code: id } })), total: 2, page: 1, pageSize: 20 });
    if (url.pathname === '/api/workspace/ownership/transfer/eligibility') return json({ allowed: true });
    if (url.pathname === '/api/workspace/ownership/transfer/accounts') return json([{ accountId: 'recipient', code: 'recipient', name: '接收人', departmentId: null }]);
    if (url.pathname === '/api/workspace/ownership/transfer' && request.method === 'POST') return json({ succeeded: true, changed: 2 });
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
  const catalog: Record<string, string> = { ...platformTestCatalogs.zhs };
  for (const owner of [...owners, 'Authorization']) for (const dictionary of documents(owner, 'dictionaries'))
    for (const [prefix, values] of Object.entries(dictionary)) if (values && typeof values === 'object')
      for (const [key, value] of Object.entries(values)) if (value && typeof value === 'object' && 'zhs' in value)
        catalog[`${prefix}.${key}`] = (value as { zhs: string }).zhs;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const wrap = (content: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={grants}><CurrentPermissionScope><RuntimeMetadataProvider value={metadata}>
      <EntryNavigationProvider><PageComposition>{content}</PageComposition></EntryNavigationProvider>
    </RuntimeMetadataProvider></CurrentPermissionScope></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const entry = entries.find(item => item.path === '/parts')!;
  expect(entry.entryType).toBe('standard-list');
  const router = createMemoryRouter([{ path: entry.path, element: wrap(
    <FeaturePlacementContext.Provider value={{ entryPosition: entry.featurePosition! }}>{renderPageEntry(entry)}</FeaturePlacementContext.Provider>) }], { initialEntries: ['/parts'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'transfers-cleanup'); vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="original-eid"]')).not.toBeNull());
  return { container, router, requests };
}


async function openAction(container: HTMLElement, label: string) {
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="original-eid"] button[aria-label="更多"]')!.click());
  const action = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent?.trim() === label);
  await vi.waitFor(() => expect(action()).toBeDefined());
  await act(async () => action()!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).not.toBeNull());
}

it('uses the actual independent Part standard-list entry to read recipient candidates without a fabricated action instance', async () => {
  const { container, requests } = await mount();
  await openAction(container, '转交');
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"] [role="combobox"]')).not.toBeNull());
  await vi.waitFor(() => expect(requests.filter(request => new URL(request.url).pathname.endsWith('/transfer/accounts'))).toHaveLength(1));
});

it('saves existing owner authorization from the actual independent Part entry without a fabricated action instance', async () => {
  const { container, requests } = await mount(true);
  await openAction(container, '对象授权');
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'PUT')).toHaveLength(1));
});

it('completes one transfer through the actual independent Part route and its owned action lifecycle', async () => {
  const { container, requests } = await mount(false, 'owner');
  await openAction(container, '转交');
  await vi.waitFor(async () => {
    await act(async () => undefined);
    expect(requests.filter(request => new URL(request.url).pathname.endsWith('/transfer/accounts'))).toHaveLength(1);
    expect(document.querySelector('[role="dialog"] .ant-select-disabled')).toBeNull();
  });
  await act(async () => document.querySelector('[role="dialog"] [role="combobox"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.querySelector('[title="接收人 (recipient)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="接收人 (recipient)"]')!.click());
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  const saves = requests.filter(request => request.method === 'POST' && new URL(request.url).pathname === '/api/part/parts/original-eid/transfer');
  expect(saves).toHaveLength(1);
  expect(saves[0].headers.get('X-NewP-Feature')).toBe(fixture('PartTransferFeatures').transferPosition);
  expect(await saves[0].clone().json()).toEqual({ recipientId: 'recipient' });
  await vi.waitFor(async () => { await act(async () => undefined); expect(requests.filter(request => new URL(request.url).pathname === '/api/part/parts').length).toBeGreaterThan(1); });
});
