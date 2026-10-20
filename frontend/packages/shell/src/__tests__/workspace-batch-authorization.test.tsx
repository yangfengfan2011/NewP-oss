import { toolbarMenuAction, toolbarActionDisabled } from './toolbarTestActions';
// @vitest-environment jsdom
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import type { StandardCrudActionDefinition } from '@icp-platform/sdk';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import { StandardCrudHostEnvironmentContext } from '../standard-crud/standardCrudHostEnvironment';
import { ActionInstanceContext } from '../composition/pageCompositionRuntime';
import { createEmbeddedHostEnvironment } from './standardCrudHostEnvironmentTestSupport';
import { platformTestCatalogs } from './localizationTestCatalogs';
import dataScopeDictionary from '../../../../modules/platform/authorization/metadata/dictionaries/DataScope.dic.json';
import workspace from '../../../../modules/infor-center/workspace/src';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';
import { workspaceClipboard } from '../../../../modules/infor-center/workspace/src/workspaceClipboard';
import workspaceRootResource from '../../../../modules/infor-center/workspace/src/workspaceRootResource';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const source = { spaceId: 'public', rootFolderId: 'root', linkIds: ['container'] };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

async function waitFor(check: () => void | Promise<void>) {
  await vi.waitFor(async () => { await act(async () => undefined); await check(); });
}

async function mount(prepare?: (body: Record<string, unknown>) => unknown, save?: () => Response, current?: () => Response | Promise<Response>) {
  const view = JSON.parse(readFileSync(resolve(__dirname, '../../../../modules/infor-center/workspace/metadata/views/WorkspaceFolderList.view.json'), 'utf8')).view;
  const action = (view.pageActions as StandardCrudActionDefinition[]).find(item => item.key === 'batchAuthorize');
  expect(action).toMatchObject({ handlerKey: 'authorize', inputKey: 'batchAuthorization', requiresSelection: true });
  const positions = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceBatchPermissionFeatures.contract.json'), 'utf8')) as { readPosition: string; batchPosition: string; copyPosition: string };
  const requests: Request[] = [];
  let actorId = 'manager';
  let refreshIdentity!: () => Promise<void>;
  let changeSource!: () => void;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const path = new URL(request.url).pathname;
    if (path.endsWith('/current-user')) return json({ id: actorId, ecode: actorId, ename: 'Manager', revision: 1, mustChangePassword: false, featurePositions: [positions.readPosition, positions.batchPosition, positions.copyPosition] });
    if (path === '/api/links/workspace.content') return json({ items: ['part-first', 'part-second', 'document-first'].map((linkId, index) => ({ linkId,
      associatedObject: { modelKey: index < 2 ? 'Part' : 'Document', id: index < 2 ? 'part' : 'document' },
      fields: { ObjectType: index < 2 ? 'Part' : 'Document', Name: `Selected ${linkId}` } })), total: 3, page: 1, pageSize: 20 });
    if (path.endsWith('/catalog')) return json({ actions: [], subjectTypes: [] });
    if (path.endsWith('/antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('/clipboard/validate')) { const body = await request.clone().json(); return json({ items: body.linkIds.map((linkId: string) => ({ linkId, modelKey: 'WorkspaceFolder' })) }); }
    if (path.endsWith('/permissions/batch/prepare') && prepare) return json(prepare(await request.clone().json()));
    if (path.endsWith('/permissions/batch/prepare')) return json({ total: 2, items: [
      { resourceKey: 'part.part', objectId: 'part', name: 'Part A', code: 'P', manageable: true, source: { position: source, linkIds: ['part-first'] } },
      { resourceKey: 'document.document', objectId: 'document', name: 'Document B', code: 'D', manageable: true, source: { position: source, linkIds: ['document-first'] } },
    ], catalogs: ['part.part', 'document.document'].map(resourceKey => ({ resourceKey,
      actions: [{ key: 'read', title: 'Read', enabled: true }], subjectTypes: [{ key: 'all', title: 'All', requiresId: false }] })) });
    if (path.endsWith('/permissions/batch/current') && current) return current();
    if (path.endsWith('/permissions/batch/current')) return json(['part.part', 'document.document'].map(resourceKey => ({ resourceKey, objectId: resourceKey === 'part.part' ? 'part' : 'document', subjects: [{ id: 'saved-' + resourceKey, type: 'all', subjectId: null, priority: 9, actions: { read: 'deny' }, expiresAt: null }] })));
    if (path.endsWith('/permissions/batch') && save) return save();
    if (path.endsWith('/permissions/batch')) return json({ succeeded: true, targets: 2, changed: 0 });
    throw new Error(`Unexpected batch request ${request.method} ${path}`);
  }));
  const instance = new ActionInstanceScope();
  const deactivate = instance.activate();
  const element = document.createElement('div'); document.body.append(element);
  const root = createRoot(element);
  const documents = (folder: string) => {
    const path = resolve(__dirname, '../../../../modules/infor-center/workspace/metadata', folder);
    return readdirSync(path).filter(file => file.endsWith('.json')).map(file => JSON.parse(readFileSync(resolve(path, file), 'utf8')));
  };
  const dictionary = documents('dictionaries').find(document => document.Workspace)!.Workspace;
  const catalog = { ...Object.fromEntries(Object.entries(dictionary).map(([key, value]) => [`Workspace.${key}`, (value as { zhs: string }).zhs])),
    ...Object.fromEntries(Object.entries(dataScopeDictionary.DataScope).map(([key, value]) => [`DataScope.${key}`, value.zhs])) };
  const environment = createEmbeddedHostEnvironment({ input: { queryScope: { ...source, anchorId: 'container-eid', linkIds: JSON.stringify(source.linkIds) } } });
  function Consumer() {
    refreshIdentity = useAuth().retrySessionValidation;
    const [currentEnvironment, setEnvironment] = useState(environment);
    changeSource = () => setEnvironment(createEmbeddedHostEnvironment({ input: { queryScope: {
      ...source, anchorId: 'other-container-eid', linkIds: JSON.stringify(['other-container']),
    } } }));
    return <StandardCrudHostEnvironmentContext.Provider value={currentEnvironment}>
      <StandardCrudListPageHost viewKey={view.key} registration={workspaceRootResource} />
    </StandardCrudHostEnvironmentContext.Provider>;
  }
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: 'Workspace', version: '1' }, ...workspace }], 'batch-header');
  onTestFinished(async () => {
    const declaration = formalDataAccessFor('workspace.folder.list', 'pageActions', 'batchAuthorize');
    for (const request of requests.filter(value => new URL(value.url).pathname.includes('/permissions/batch')
      || new URL(value.url).pathname.includes('/permissions/subjects/')))
      expect(request.headers.get('X-NewP-Data-Declaration'), new URL(request.url).pathname).toBe(declaration.id);
    await act(async () => root.unmount()); deactivate(); element.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'batch-clean'); });
  await act(async () => root.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: 'Chinese' }]} catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...catalog } }}>
    <AuthProvider initialUser={{ id: 'manager', ecode: 'manager', ename: 'Manager', revision: 1, mustChangePassword: false, featurePositions: [positions.readPosition, positions.batchPosition, positions.copyPosition] }}>
      <CurrentPermissionScope><AuthorizationProvider featurePositions={[positions.readPosition, positions.batchPosition, positions.copyPosition]}><PlatformQueryProvider scopeKey="batch-manager">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
          { key: 'workspace', kind: 'navigation', position: positions.readPosition },
          { key: 'batchAuthorize', kind: 'action', position: positions.batchPosition, parentPosition: positions.readPosition, staticScope: 'folder', actionScope: 'list.pageActions' },
        ] }] }, navDocuments: documents('navigation'), viewDocuments: documents('views'), modelDocuments: [
          JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json'), 'utf8')),
          ...documents('models').filter(document => document.WorkspaceFolderContent),
        ] }}>
          <FeaturePlacementContext.Provider value={{ entryPosition: positions.readPosition, staticScope: 'folder' }}>
            <ActionInstanceContext.Provider value={instance}><Consumer /></ActionInstanceContext.Provider>
          </FeaturePlacementContext.Provider>
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider></CurrentPermissionScope>
    </AuthProvider>
  </LocalizationProvider></MemoryRouter>));
  for (const id of ['part-first', 'part-second', 'document-first']) {
    await waitFor(() => expect(element.querySelector(`tr[data-row-key="${id}"] input[type="checkbox"]`)).not.toBeNull());
    await act(async () => element.querySelector<HTMLInputElement>(`tr[data-row-key="${id}"] input[type="checkbox"]`)!.click());
  }
  const open = () => toolbarMenuAction(element, catalog['Workspace.AccessAndOwnership'], catalog['Workspace.BatchAuthorize']);
  await waitFor(async () => expect(toolbarActionDisabled(await open())).toBe(false));
  const batchAction = await open();
  await act(async () => batchAction!.click());
  await waitFor(() => expect(document.body.textContent).toContain('Part A'));
  expect(document.body.textContent).toContain('Document B');
  expect(document.querySelector<HTMLInputElement>('input[type="radio"][value="U"]')?.checked).toBe(true);
  return { requests, positions, instance, element, changeBoundary: async (boundary: 'account' | 'source') => {
    if (boundary === 'source') changeSource();
    else { actorId = 'other-manager'; await refreshIdentity(); }
  } };
}

it('opens the actual Workspace batch header with canonical targets and submits one atomic request', async () => {
  const { requests, positions } = await mount();
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  const saves = requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'));
  expect(saves).toHaveLength(1);
  expect(saves[0].method).toBe('POST');
  expect(saves[0].headers.get('X-NewP-Feature')).toBe(positions.batchPosition);
  const declaration = formalDataAccessFor('workspace.folder.list', 'pageActions', 'batchAuthorize');
  for (const request of requests.filter(value => new URL(value.url).pathname.includes('/permissions/batch')))
    expect(request.headers.get('X-NewP-Data-Declaration'), new URL(request.url).pathname).toBe(declaration.id);
  const payload = await saves[0].clone().json();
  expect(payload.mode).toBe('U');
  expect(payload.sources).toHaveLength(2);
  expect(payload.resources.map((resource: { resourceKey: string }) => resource.resourceKey).sort()).toEqual(['document.document', 'part.part']);
  expect(requests.some(request => request.method === 'PUT')).toBe(false);
  await waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
});

it('appends clipboard sources, expands server descendants and submits only the filtered final set after removal', async () => {
  const item = (resourceKey: string, objectId: string, name: string) => ({ resourceKey, objectId, name, code: null, manageable: true,
    source: { position: source, linkIds: [`${objectId}-link`] } });
  const { requests, instance, positions } = await mount(body => {
    let items = [item('part.part', 'part', 'Part A'), item('document.document', 'document', 'Document B')];
    if ((body.sources as unknown[]).length > 1) items.push(item('workspace.folder', 'folder', 'Clipboard folder'));
    if (body.includeDescendants) items.push(item('document.document', 'child', 'Server descendant'));
    if ((body.filters as { resourceKeys?: string[] } | undefined)?.resourceKeys?.length) items = items.filter(value => value.resourceKey === 'document.document');
    return { total: items.length, items, catalogs: [...new Set(items.map(value => value.resourceKey))].map(resourceKey => ({ resourceKey,
      actions: [{ key: 'read', title: 'Read', enabled: true }], subjectTypes: [{ key: 'all', title: 'All', requiresId: false }] })) };
  });
  await act(async () => { await workspaceClipboard(instance).copy({ ...source, linkIds: ['other-container'] }, ['clipboard-folder'], positions.copyPosition); });
  const click = async (label: string) => {
    const button = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(value => value.textContent?.replace(/\s/g, '') === label);
    expect(button, label).toBeDefined();
    await act(async () => button!.click());
  };
  await click('追加剪贴板');
  await waitFor(() => expect(document.body.textContent).toContain('Clipboard folder'));
  await click('加入文件夹后代');
  await waitFor(() => expect(document.body.textContent).toContain('Server descendant'));
  const remove = document.querySelector<HTMLButtonElement>('button[aria-label="移除 Part A"]');
  expect(remove).not.toBeNull();
  await act(async () => remove!.click());
  const type = document.querySelector<HTMLInputElement>('input[type="checkbox"][value="document.document"]');
  expect(type).not.toBeNull();
  await act(async () => type!.click());
  await click('应用集合筛选');
  await waitFor(() => expect(document.body.textContent).not.toContain('Clipboard folder'));
  await act(async () => document.querySelector<HTMLInputElement>('input[type="checkbox"][value="document.document"]')!.click());
  await click('应用集合筛选');
  await waitFor(() => expect(document.body.textContent).toContain('Clipboard folder'));
  expect(document.querySelector('button[aria-label="移除 Part A"]')).toBeNull();
  const unfiltered = await requests.filter(request => new URL(request.url).pathname.endsWith('/batch/prepare')).at(-1)!.clone().json();
  expect(unfiltered.filters.resourceKeys).toBeUndefined();
  await act(async () => document.querySelector<HTMLInputElement>('input[type="checkbox"][value="document.document"]')!.click());
  await click('应用集合筛选');
  await waitFor(() => expect(document.body.textContent).not.toContain('Clipboard folder'));
  const prepares = requests.filter(request => new URL(request.url).pathname.endsWith('/batch/prepare'));
  const last = await prepares.at(-1)!.clone().json();
  expect(last).toMatchObject({ includeDescendants: true, filters: { resourceKeys: ['document.document'] } });
  expect(last.sources).toHaveLength(2);
  await act(async () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary')!.click());
  await waitFor(() => expect(requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'))).toHaveLength(1));
  const saved = await requests.find(request => new URL(request.url).pathname.endsWith('/permissions/batch'))!.clone().json();
  expect(saved.sources.flatMap((value: { linkIds: string[] }) => value.linkIds).sort()).toEqual(['child-link', 'document-link']);
  expect(saved.resources.map((value: { resourceKey: string }) => value.resourceKey)).toEqual(['document.document']);
});

it('saves read subjects for both resources through the real inline editors', async () => {
  const { requests } = await mount();
  const sections = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
    .filter(button => button.textContent === '添加主体项').map(button => button.closest('section')!);
  expect(sections).toHaveLength(2);
  for (const section of sections) {
    await act(async () => [...section.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === '添加主体项')!.click());
    await waitFor(() => expect(section.querySelector('[data-subject-editor]')?.getAttribute('aria-label')).toBe('添加授权主体'));
    expect(document.querySelector('.ant-drawer-content')).toBeNull();
    const editor = section.querySelector('[data-subject-editor]')!;
    await act(async () => [...editor.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '确定')!.click());
    await waitFor(() => expect(section.querySelector('[data-subject-editor]')).toBeNull());
    await act(async () => section.querySelector<HTMLButtonElement>('button[role="checkbox"][aria-label="读取：不选"]')!.click());
    expect(section.querySelector('button[role="checkbox"][aria-label="读取：同意"]')).not.toBeNull();
  }
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await waitFor(() => expect(requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'))).toHaveLength(1));
  const payload = await requests.find(request => new URL(request.url).pathname.endsWith('/permissions/batch'))!.clone().json();
  expect(payload.mode).toBe('U');
  expect(payload.resources).toHaveLength(2);
  for (const resource of payload.resources) expect(resource.subjects).toEqual([{ type: 'all', subjectId: null, priority: 0, actions: { read: 'allow' }, expiresAt: null }]);
});

it('edits an actual subject with a full U template and carries R clear confirmation through the standard review', async () => {
  const { requests } = await mount();
  const section = document.querySelector('section')!;
  const add = [...section.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === '添加主体项');
  expect(add).toBeDefined();
  await act(async () => add!.click());
  await waitFor(() => expect(document.querySelector('[data-subject-editor]')).not.toBeNull());
  const confirmSubject = [...document.querySelectorAll<HTMLButtonElement>('[data-subject-editor] button')].find(button => button.textContent?.replace(/\s/g, '') === '确定');
  expect(confirmSubject?.disabled).toBe(false);
  await act(async () => confirmSubject!.click());
  await waitFor(() => expect(section.querySelector('button[role="checkbox"]')).not.toBeNull());
  await act(async () => section.querySelector<HTMLButtonElement>('button[role="checkbox"]')!.click());
  await act(async () => document.querySelector<HTMLInputElement>('input[type="radio"][value="R"]')!.click());
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await waitFor(() => expect(confirm()?.disabled).toBe(true));
  const clear = [...document.querySelectorAll<HTMLLabelElement>('[role="dialog"] label')].find(label => label.textContent?.includes('确认清空'))?.querySelector<HTMLInputElement>('input');
  expect(clear).not.toBeNull();
  await act(async () => clear!.click());
  await waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/permissions/batch'))).toBe(true));
  const payload = await requests.find(request => new URL(request.url).pathname.endsWith('/permissions/batch'))!.clone().json();
  expect(payload).toMatchObject({ mode: 'R', confirmClear: true });
  expect(payload.resources[0].subjects).toEqual([{ type: 'all', subjectId: null, priority: 0, actions: { read: 'allow' }, expiresAt: null }]);
  expect(payload.resources[1].subjects).toEqual([]);
});

it.each(['确定', '取消'] as const)('keeps the batch blocked while another resource is editing after %s closes the first editor', async firstAction => {
  const { requests } = await mount();
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  const saves = () => requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'));
  const sections = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
    .filter(button => button.textContent === '添加主体项').map(button => button.closest('section')!);
  expect(sections).toHaveLength(2);
  for (const section of sections) {
    await act(async () => [...section.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === '添加主体项')!.click());
    await waitFor(() => expect(section.querySelector('[data-subject-editor]')).not.toBeNull());
    await act(async () => section.querySelector<HTMLButtonElement>('[data-subject-editor] button[role="checkbox"]')!.click());
  }
  expect(document.querySelectorAll('[data-subject-editor]')).toHaveLength(2);
  await waitFor(() => expect(confirm()?.disabled).toBe(true));
  const closeEditor = (section: Element, action: string) => [...section.querySelectorAll<HTMLButtonElement>('[data-subject-editor] button')]
    .find(button => button.textContent?.replace(/\s/g, '') === action)!;
  await act(async () => closeEditor(sections[0], firstAction).click());
  await waitFor(() => expect(sections[0].querySelector('[data-subject-editor]')).toBeNull());
  expect(sections[1].querySelector('[data-subject-editor]')).not.toBeNull();
  await waitFor(() => expect(confirm()?.disabled).toBe(true));
  await act(async () => confirm()!.click());
  expect(saves()).toHaveLength(0);
  await act(async () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button:not(.ant-btn-primary)')!.click());
  await waitFor(() => expect(document.body.textContent).toContain('放弃未保存的草稿'));
  const keepEditing = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
    .find(button => button.textContent?.replace(/\s/g, '') === '继续编辑');
  await act(async () => keepEditing!.click());
  await waitFor(() => expect(getComputedStyle(keepEditing!.closest<HTMLElement>('.ant-modal-wrap')!).display).toBe('none'));
  expect(sections[1].querySelector('[data-subject-editor]')).not.toBeNull();
  expect(confirm()?.disabled).toBe(true);

  await act(async () => closeEditor(sections[1], '确定').click());
  await waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await waitFor(() => expect(saves()).toHaveLength(1));
  const payload = await saves()[0].clone().json();
  const subject = { type: 'all', subjectId: null, priority: 0, actions: { read: 'allow' }, expiresAt: null };
  expect(payload).toMatchObject({ mode: 'U', confirmClear: false });
  expect(payload.resources).toEqual([
    { resourceKey: 'part.part', subjects: firstAction === '确定' ? [subject] : [] },
    { resourceKey: 'document.document', subjects: [subject] },
  ]);
});

it('requires explicit whole-set current reconciliation after an unknown save and never replays the old template', async () => {
  const { requests } = await mount(undefined, () => json({ code: 'Persistence.CommitOutcomeUnknown' }, 409));
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await waitFor(() => expect(confirm()?.disabled).toBe(true));
  const reconcile = () => [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(button => button.textContent?.replace(/\s/g, '') === '核对当前整组配置');
  expect(reconcile()).toBeDefined();
  await act(async () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button:not(.ant-btn-primary)')!.click());
  await waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  const open = await toolbarMenuAction(document.body, '授权与归属', '批量授权');
  await act(async () => open!.click());
  await waitFor(() => expect(reconcile()).toBeDefined());
  expect(confirm()?.disabled).toBe(true);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/batch/prepare'))).toHaveLength(1);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/batch/current'))).toHaveLength(0);
  await act(async () => reconcile()!.click());
  await waitFor(() => expect(document.body.textContent).toContain('当前配置不能证明原请求是否提交'));
  expect(confirm()?.disabled).toBe(true);
  expect(document.querySelectorAll('button[role="checkbox"][aria-checked="mixed"]')).toHaveLength(2);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'))).toHaveLength(1);
  const restart = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(button => button.textContent?.replace(/\s/g, '') === '开始新的授权操作');
  expect(restart).toBeDefined();
  await act(async () => restart!.click());
  await waitFor(() => expect(document.querySelectorAll('button[role="checkbox"][aria-checked="mixed"]')).toHaveLength(0));
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'))).toHaveLength(1);
});

it('protects an edited final target set with the existing standard discard confirmation', async () => {
  await mount();
  const remove = document.querySelector<HTMLButtonElement>('button[aria-label="移除 Part A"]');
  await act(async () => remove!.click());
  const cancel = document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button:not(.ant-btn-primary)');
  await act(async () => cancel!.click());
  await waitFor(() => expect(document.body.textContent).toContain('放弃未保存的草稿'));
  const keep = [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(button => button.textContent?.replace(/\s/g, '') === '继续编辑');
  await act(async () => keep!.click());
  await waitFor(() => expect(document.querySelector('button[aria-label="移除 Document B"]')).not.toBeNull());
  expect(document.querySelector('button[aria-label="移除 Part A"]')).toBeNull();
});

it('withdraws cached batch contents when explicit whole-set reconciliation is no longer authorized', async () => {
  const { requests } = await mount(undefined, () => json({ code: 'Persistence.CommitOutcomeUnknown' }, 409), () => json({ code: 'FORBIDDEN' }, 403));
  await act(async () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary')!.click());
  const reconcile = () => [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(button => button.textContent?.replace(/\s/g, '') === '核对当前整组配置');
  await waitFor(() => expect(reconcile()).toBeDefined());
  await act(async () => reconcile()!.click());
  await waitFor(() => expect(document.body.textContent).not.toContain('Part A'));
  expect(document.body.textContent).not.toContain('Document B');
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'))).toHaveLength(1);
});

it.each(['account', 'source'] as const)('aborts current reconciliation and removes old batch contents after the real %s boundary changes', async boundary => {
  let finishCurrent!: (response: Response) => void;
  const { requests, changeBoundary } = await mount(undefined,
    () => json({ code: 'Persistence.CommitOutcomeUnknown' }, 409),
    () => new Promise<Response>(resolve => { finishCurrent = resolve; }));
  const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
  await waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  const reconcile = () => [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '核对当前整组配置');
  await waitFor(() => expect(reconcile()).toBeDefined());
  await act(async () => reconcile()!.click());
  await waitFor(() => expect(finishCurrent).toBeTypeOf('function'));
  const request = requests.find(request => new URL(request.url).pathname.endsWith('/batch/current'))!;
  await act(async () => changeBoundary(boundary));
  await waitFor(() => expect(request.signal.aborted).toBe(true));
  await waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  await act(async () => finishCurrent(json(['part.part', 'document.document'].map(resourceKey => ({ resourceKey,
    objectId: resourceKey === 'part.part' ? 'part' : 'document', subjects: [{ id: 'late-private-subject', type: 'all',
      subjectId: null, priority: 99, actions: { read: 'deny' }, expiresAt: null }] })))));
  expect(document.querySelector('[role="dialog"]')).toBeNull();
  expect(document.body.textContent).not.toContain('Part A');
  expect(document.body.textContent).not.toContain('Document B');
  expect(document.querySelector('tr[data-row-key="late-private-subject"]')).toBeNull();
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/permissions/batch'))).toHaveLength(1);
});
