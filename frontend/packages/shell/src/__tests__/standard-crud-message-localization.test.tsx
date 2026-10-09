// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, onTestFinished, vi } from 'vitest';
import { LanguageSelect, LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import { useStandardCrudLookupRuntime } from '../standard-crud/standardCrudLookupRuntime';
import type { StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import dictionary from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';
import workspaceDictionary from '../../../../../modules/Workspace/metadata/dictionaries/Workspace.dic.json';
import partDictionary from '../../../../../modules/Part/metadata/dictionaries/Part.dic.json';
import documentDictionary from '../../../../../modules/Document/metadata/dictionaries/Document.dic.json';
import workspaceResource from '../../../../modules/workspace/src/workspaceRootResource';
// 装载真实模块输入，避免冷转换落入交互测试预算；错误仍由标准动作与Lookup呈现。
import '../../../../modules/workspace/src/FolderCandidateInput';

vi.unmock('@icp-platform/core');

const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(Object.entries(dictionary.Platform).map(([key, value]) => [`Platform.${key}`, value[language as 'zhs' | 'en']]))]));

it.each(['write-denied', 'part-read-denied', 'document-read-denied'] as const)('retranslates the real Workspace %s error while retaining the candidate draft and without replaying commands', async scenario => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const requests: { path: string; method: string; body?: unknown }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    requests.push({ path: url.pathname, method, body: method === 'POST' ? input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)) : undefined });
    if (url.pathname.endsWith('antiforgery-token')) return new Response(JSON.stringify({ requestToken: 'token' }));
    if (url.pathname === '/api/workspace/folders/candidates') return new Response(JSON.stringify({ items: [{ id: 'draft-object', name: '保留的业务草稿' }], total: 1, page: 1, pageSize: 20 }));
    if (url.pathname === '/api/workspace/content/references') return new Response(JSON.stringify({ title: 'Workspace.ParentWriteDenied', code: 'Workspace.ParentWriteDenied', reasonKey: 'Workspace.ParentWriteDenied', status: 403 }), { status: 403 });
    throw new Error(`Unexpected request ${url.pathname}`);
  }));
  const workspaceCatalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, { ...catalogs[language], ...Object.fromEntries([
    [workspaceDictionary.Workspace, 'Workspace'], [partDictionary.Part, 'Part'], [documentDictionary.Document, 'Document'],
  ].flatMap(([entries, prefix]) => Object.entries(entries).map(([key, value]) => [`${prefix}.${key}`, (value as { zhs: string; en: string })[language as 'zhs' | 'en']]))) }]));
  const refresh = vi.fn();
  function Consumer() {
    const { t } = useLocalization();
    return <><LanguageSelect /><StandardCrudActions actions={[{ key: 'add', labelKey: t('Workspace.AddFolder'), inputKey: 'folderCandidate' }]}
      handlers={workspaceResource.actionHandlers} actionInputs={workspaceResource.actionInputRenderers}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'workspace-add' })} refresh={refresh}
      context={{ id: 'root', queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' } }} /></>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
  await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={workspaceCatalogs}>
    <PlatformQueryProvider scopeKey="workspace-message"><Consumer /></PlatformQueryProvider>
  </LocalizationProvider>));
  await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
  await vi.waitFor(() => expect(document.querySelectorAll('.ant-modal .ant-select-selector')).toHaveLength(2));
  if (scenario === 'write-denied') {
    await act(async () => document.querySelectorAll<HTMLElement>('.ant-modal .ant-select-selector')[1].dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
    await vi.waitFor(() => expect([...document.querySelectorAll('.ant-select-item-option-content')].some(node => node.textContent === '保留的业务草稿')).toBe(true));
    await act(async () => [...document.querySelectorAll<HTMLElement>('.ant-select-item-option-content')].find(node => node.textContent === '保留的业务草稿')!.click());
    await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-footer .ant-btn-primary')!.click());
  } else {
    const label = scenario === 'part-read-denied' ? '零组件' : '图文档';
    await act(async () => document.querySelectorAll<HTMLElement>('.ant-modal .ant-select-selector')[0].dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
    await vi.waitFor(() => expect([...document.querySelectorAll('.ant-select-item-option-content')].some(node => node.textContent === label)).toBe(true));
    await act(async () => [...document.querySelectorAll<HTMLElement>('.ant-select-item-option-content')].find(node => node.textContent === label)!.click());
  }
  const chinese = scenario === 'write-denied' ? '没有编辑当前文件夹内容的权限。' : '没有读取此对象类型的权限，请选择其他类型或联系管理员。';
  const english = scenario === 'write-denied' ? 'You do not have permission to edit the contents of this folder.' : 'You do not have permission to read this object type. Choose another type or contact an administrator.';
  await vi.waitFor(() => expect(document.body.textContent).toContain(chinese));
  const modal = document.querySelector('.ant-modal');
  await act(async () => { const select = container.querySelector('select')!; select.value = 'en'; select.dispatchEvent(new Event('change', { bubbles: true })); });
  await vi.waitFor(() => expect(document.body.textContent).toContain(english));
  expect(document.querySelector('.ant-modal')).toBe(modal);
  if (scenario === 'write-denied') {
    expect(document.querySelectorAll('.ant-modal .ant-select-selection-item')[1]?.textContent).toBe('保留的业务草稿');
    expect(requests.filter(request => request.method === 'POST')).toEqual([{ path: '/api/workspace/content/references', method: 'POST', body: { position: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, target: { modelKey: 'Workspace/WorkspaceFolder', id: 'draft-object' } } }]);
  } else {
    expect(document.querySelector('.ant-modal .ant-select-selection-item')?.textContent).toBe(scenario === 'part-read-denied' ? 'Parts' : 'Documents');
    expect(document.querySelector<HTMLButtonElement>('.ant-modal-footer .ant-btn-primary')!.disabled).toBe(true);
    expect(requests.some(request => request.path.startsWith('/api/part/') || request.path.startsWith('/api/document/') || request.method === 'POST')).toBe(false);
  }
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
