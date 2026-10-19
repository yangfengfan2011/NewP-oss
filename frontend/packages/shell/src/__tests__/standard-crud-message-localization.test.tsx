// @vitest-environment jsdom
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, onTestFinished, vi } from 'vitest';
import { LanguageSelect, LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import * as core from '@icp-platform/core';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import { useStandardCrudLookupRuntime } from '../standard-crud/standardCrudLookupRuntime';
import type { StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import dictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
const workspaceDictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');
const partDictionary = readComponentJson('Part', 'metadata/dictionaries/Part.dic.json');
const documentDictionary = readComponentJson('Document', 'metadata/dictionaries/Document.dic.json');
const { default: workspaceResource } = await loadComponentSource<{ default: StandardCrudResourceRegistration }>('Workspace', 'workspaceRootResource.ts');
// 装载真实模块输入，避免冷转换落入交互测试预算；错误仍由标准动作与Lookup呈现。
await loadComponentSource('Workspace', 'WorkspaceObjectCreateInput.tsx');
await loadComponentSource('Workspace', 'WorkspaceDocumentCreateInput.tsx');

vi.unmock('@icp-platform/core');

const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(Object.entries(dictionary.Platform).map(([key, value]) => [`Platform.${key}`, value[language as 'zhs' | 'en']]))]));

it.each(['createPart', 'createDocument'] as const)('retranslates the real Workspace %s error while retaining the candidate draft and without replaying commands', async scenario => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const uploadId = 'FileUploadLease_0123456789abcdef0123456789abcdef';
  const upload = scenario === 'createDocument' ? vi.spyOn(core, 'platformUpload').mockResolvedValue(new Response(JSON.stringify({
    uploadId, fileId: 'FileRecord_internal', fileName: 'main.pdf', length: 4, contentType: 'application/pdf', expiresAt: '2026-10-14T00:00:00Z'
  }))) : undefined;
  onTestFinished(() => upload?.mockRestore());
  const dataAccess = formalDataAccessFor('workspace.folder.list', 'pageActions', scenario);
  const featurePosition = dataAccess.featurePositions.find(position => position.includes('surface:list.pageActions'))!;
  const path = '/api/workspace/content/' + (scenario === 'createPart' ? 'parts' : 'documents');
  const requests: { path: string; method: string; body?: unknown }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    requests.push({ path: url.pathname, method, body: method === 'POST' ? input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)) : undefined });
    if (url.pathname.endsWith('antiforgery-token')) return new Response(JSON.stringify({ requestToken: 'token' }));
    if (url.pathname === '/api/workspace/folders/candidates') return new Response(JSON.stringify({ items: [{ id: 'draft-object', name: '保留的业务草稿' }], total: 1, page: 1, pageSize: 20 }));
    if (url.pathname === path) return new Response(JSON.stringify({ title: 'Workspace.ParentWriteDenied', code: 'Workspace.ParentWriteDenied', reasonKey: 'Workspace.ParentWriteDenied', status: 403 }), { status: 403 });
    throw new Error(`Unexpected request ${url.pathname}`);
  }));
  const workspaceCatalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, { ...catalogs[language], ...Object.fromEntries([
    [workspaceDictionary.Workspace, 'Workspace'], [partDictionary.Part, 'Part'], [documentDictionary.Document, 'Document'],
  ].flatMap(([entries, prefix]) => Object.entries(entries).map(([key, value]) => [`${prefix}.${key}`, (value as { zhs: string; en: string })[language as 'zhs' | 'en']]))) }]));
  const refresh = vi.fn();
  function Consumer() {
    const { t } = useLocalization();
    return <><LanguageSelect /><StandardCrudActions actions={[{ key: scenario, labelKey: t(scenario === 'createPart' ? 'Workspace.CreatePart' : 'Workspace.CreateDocument'), inputKey: 'createObject', dataAccess }]}
      handlers={workspaceResource.actionHandlers} actionInputs={workspaceResource.actionInputRenderers}
      featureResolutionForAction={() => ({ kind: 'allowed', position: featurePosition })} refresh={refresh}
      context={{ id: 'root', queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' } }} /></>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
  await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={workspaceCatalogs}>
    <PlatformQueryProvider scopeKey="workspace-message"><Consumer /></PlatformQueryProvider>
  </LocalizationProvider>));
  await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer input[id="ECODE"]')).not.toBeNull());
  for (const [key, value] of [['ECODE', 'DRAFT'], ['ENAME', '保留的业务草稿']]) {
    await act(async () => {
      const input = document.querySelector<HTMLInputElement>(`.ant-drawer input[id="${key}"]`)!;
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  }
  if (scenario === 'createDocument') {
    await act(async () => {
      const input = document.querySelector<HTMLInputElement>('.ant-drawer input[type="file"]')!;
      Object.defineProperty(input, 'files', { configurable: true, value: [new File(['main'], 'main.pdf', { type: 'application/pdf' })] });
      input.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.disabled).toBe(false);
    });
  }
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.click());
  const chinese = '没有编辑当前文件夹内容的权限。';
  const english = 'You do not have permission to edit the contents of this folder.';
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer-body')?.textContent).toContain(chinese));
  const modal = document.querySelector('.ant-drawer');
  await act(async () => { const select = container.querySelector('select')!; select.value = 'en'; select.dispatchEvent(new Event('change', { bubbles: true })); });
  await vi.waitFor(() => expect(document.body.textContent).toContain(english));
  expect(document.querySelector('.ant-drawer')).toBe(modal);
  expect(document.querySelector<HTMLInputElement>('.ant-drawer input[id="ENAME"]')?.value).toBe('保留的业务草稿');
  expect(requests.filter(request => request.method === 'POST')).toEqual([{ path, method: 'POST', body: { position: { spaceId: 'public', rootFolderId: 'root', linkIds: [] },
    values: { ECODE: 'DRAFT', ENAME: '保留的业务草稿' }, ...(scenario === 'createDocument' ? { uploads: { mainUploadId: uploadId } } : {}) } }]);
  if (upload) expect(upload).toHaveBeenCalledTimes(1);
  expect(refresh).not.toHaveBeenCalled();
});

it('retranslates an existing action failure without replaying the action or exposing diagnostics', async () => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const handler = vi.fn().mockRejectedValue(new StandardCrudRequestError('private diagnostic', 403, {}, { code: 'Platform.Forbidden' }));
  const refresh = vi.fn();
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={catalogs}>
      <LanguageSelect /><StandardCrudActions actions={[{ key: 'save', labelKey: 'Execute' }]} handlers={{ save: handler }} featureResolutionForAction={() => ({ kind: 'allowed', position: 'test' })} refresh={refresh} context={{ id: 'person' }} />
    </LocalizationProvider>));
    await act(async () => container.querySelector('button')!.click());
    expect(container.textContent).toContain('当前操作已被拒绝，请核对当前权限。');
    await act(async () => {
      const select = container.querySelector('select')!; select.value = 'en';
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(container.textContent).toContain('This operation was denied. Check your permissions.');
    expect(container.textContent).not.toContain('private diagnostic');
    expect(handler).toHaveBeenCalledTimes(1); expect(refresh).not.toHaveBeenCalled();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('translates only synthetic unavailable lookup labels and preserves selected IDs and same-named business labels', async () => {
  const fields: StandardCrudFieldDefinition[] = ['missing', 'business'].map(key => ({ key, labelKey: key, modelField: key, kind: 'lookup', lookup: { providerKey: 'people' } }));
  const values = { missing: 'missing-id', business: 'business-id' };
  const load = vi.fn(async () => ({ options: [], currentOptions: [{ value: 'business-id', label: '当前值不可用', disabled: true }], page: 1, pageSize: 20, hasMore: false }));
  const providers = [{ key: 'people', load }];
  function Probe() {
    const runtime = useStandardCrudLookupRuntime({ resourceKey: 'people', fields, providers, values });
    return <StandardCrudForm formId="lookup-facts" mode="edit" fields={fields} sections={[]} values={values} lookupOptions={runtime.lookupOptions} lookupLoading={runtime.lookupLoading} onChange={vi.fn()} onSubmit={vi.fn()} saving={false} titleForSection={key => key} />;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={catalogs}>
      <LanguageSelect /><PlatformQueryProvider scopeKey="same-user"><Probe /></PlatformQueryProvider>
    </LocalizationProvider>));
    await vi.waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    await act(async () => {
      const select = container.querySelector('select')!; select.value = 'en'; select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    const selected = [...container.querySelectorAll('.ant-select-selection-item')].map(item => item.textContent);
    expect(selected).toEqual(['Current value unavailable', '当前值不可用']);
    expect([...container.querySelectorAll('.np-standard-crud-select')].map(item => item.getAttribute('data-value'))).toEqual(['missing-id', 'business-id']);
    expect(load).toHaveBeenCalledTimes(2);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
