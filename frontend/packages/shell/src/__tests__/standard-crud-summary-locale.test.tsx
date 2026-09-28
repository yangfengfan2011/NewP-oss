// @vitest-environment jsdom
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeAll, expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PlatformRequestLocaleBridge } from '../localization/PlatformRequestLocaleBridge';
import type { RuntimeMetadataManifest } from '../metadata/runtimeMetadataManifest';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import { personStandardCrudResource } from '../../../../modules/organization/src/standard-crud/personStandardCrudResource';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import detailView from '../../../../../modules/Organization/metadata/views/PersonDetail.view.json';
import personMessages from '../../../../../modules/Organization/metadata/dictionaries/Organization.Person.dic.json';
import detailMessages from '../../../../../modules/Organization/metadata/dictionaries/PersonDetailView.dic.json';
import platformMessages from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';
import featureManifest from './fixtures/organization-feature-manifest.json';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => { await import('../../../../modules/organization/src/standard-crud/personStandardCrudRenderers'); });
const manifest = featureManifest as RuntimeMetadataManifest;
const features = manifest.modules[0].features!;
const entry = features.find(point => point.kind === 'tab' && point.key === 'persons')!.position;
const link = features.find(point => point.kind === 'action' && point.parentPosition === entry && point.key === 'linkUser' && point.actionScope === 'detail.custom' && point.staticScope === null)!.position;
const disable = features.find(point => point.kind === 'action' && point.parentPosition === entry && point.key === 'disable' && point.actionScope === 'detail.pageActions' && point.staticScope === null)!.position;
const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries([
  ...Object.entries(personMessages['Organization.Person']).map(([key, value]) => [`Organization.Person.${key}`, value[language as 'zhs' | 'en']]),
  ...Object.entries(detailMessages.PersonDetailView).map(([key, value]) => [`PersonDetailView.${key}`, value[language as 'zhs' | 'en']]),
  ...Object.entries(platformMessages.Platform).map(([key, value]) => [`Platform.${key}`, value[language as 'zhs' | 'en']]),
])]));
const summaryRenderer = vi.fn(personStandardCrudResource.summaryRenderers!.userBinding);
const registration = { ...personStandardCrudResource, summaryRenderers: { ...personStandardCrudResource.summaryRenderers, userBinding: summaryRenderer } };

let setProviderLanguage: ReturnType<typeof useLocalization>['setLanguage'];
function QueryScope({ children }: { children: ReactNode }) {
  const { currentLanguage, setLanguage } = useLocalization();
  setProviderLanguage = setLanguage;
  return <PlatformQueryProvider scopeKey={currentLanguage}>{children}</PlatformQueryProvider>;
}
function Harness({ id = 'person-1', canBind = true, onDisable }: { id?: string; canBind?: boolean; onDisable?: () => Promise<void> }) {
  const resource = onDisable ? { ...registration, actionHandlers: { ...registration.actionHandlers, disable: onDisable } } : registration;
  return <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]} catalogs={catalogs}>
    <PlatformRequestLocaleBridge><QueryScope><AuthorizationProvider featurePositions={canBind ? [entry, link, disable] : [entry, disable]}>
      <RuntimeMetadataProvider value={{ manifest, navDocuments: [], viewDocuments: [detailView], modelDocuments: [runtimeModels.person] }}>
        <FeaturePlacementContext.Provider value={{ entryPosition: entry }}>
          <StandardCrudDetailPageHost id={id} viewKey="organization.person.detail" registration={resource} />
        </FeaturePlacementContext.Provider>
      </RuntimeMetadataProvider>
    </AuthorizationProvider></QueryScope></PlatformRequestLocaleBridge>
  </LocalizationProvider>;
}
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
function detail(id: string, name: string, available = true) {
  return { fields: { EID: id, PersonNo: id, ENAME: name, IsEnabled: true }, summaries: {
    userBinding: available ? { status: 'ready', data: { personEid: id, personName: name, isPersonEnabled: true, accountEid: null, account: null, isAccountReadable: true, isAccountAvailable: true } } : { status: 'dependencyUnavailable', data: null },
  } };
}
let root: Root | undefined;
let container: HTMLDivElement;
let resolveRead: (response: Response) => void;
let reads: number;
let writes: number;
afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove(); root = undefined;
  vi.unstubAllGlobals(); vi.restoreAllMocks(); summaryRenderer.mockClear();
});
async function mountDetail(onDisable?: () => Promise<void>) {
  reads = 0; writes = 0;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : String(input);
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    if (method !== 'GET') { writes += 1; throw new Error('This test must not bind an account'); }
    if (url.includes('/user-binding/candidates')) return json([{ accountEid: 'account-1', ecode: 'candidate', ename: '候选账号', status: 'Enabled', revision: 7 }]);
    reads += 1;
    if (reads === 1) return json(detail('person-1', '初始人员'));
    return new Promise<Response>(resolve => { resolveRead = resolve; });
  }));
  container = document.createElement('div'); document.body.appendChild(container);
  await act(async () => { root = createRoot(container); root.render(<Harness onDisable={onDisable} />); });
  await vi.waitFor(() => expect(button('建立永久绑定')).toBeDefined());
}
async function openDraft() {
  await mountDetail();
  await act(async () => button('建立永久绑定')!.click());
  await act(async () => button('搜索候选')!.click());
  await act(async () => document.querySelector<HTMLInputElement>('input[type="radio"]')!.click());
  const textarea = document.querySelector<HTMLTextAreaElement>('.ant-modal textarea')!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(textarea, '保留绑定草稿');
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
  });
  return textarea;
}
function button(label: string) { return [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === label.replace(/\s/g, '')); }
async function switchLanguage(code = 'en') {
  // 只验证外层真实 Provider 状态继承，不模拟穿透遮罩操作背景控件。
  await act(async () => setProviderLanguage(code));
}

it('keeps the real binding dialog, selection and draft while locale detail reads replace the snapshot', async () => {
  const textarea = await openDraft();
  await switchLanguage();
  expect(reads).toBe(2);
  expect(document.querySelector('.ant-modal')).not.toBeNull();
  expect(document.querySelector('.ant-modal textarea')).toBe(textarea);
  expect(textarea.value).toBe('保留绑定草稿');
  expect(document.querySelector<HTMLInputElement>('input[type="radio"]')?.checked).toBe(true);
  const resolveOldLanguage = resolveRead;
  await switchLanguage('zhs');
  expect(reads).toBe(3);
  await act(async () => resolveOldLanguage(json(detail('person-1', '旧语言迟到'))));
  expect(summaryRenderer.mock.lastCall?.[0].block.data).not.toMatchObject({ personName: '旧语言迟到' });
  expect(document.querySelector('.ant-modal textarea')).toBe(textarea);
  await act(async () => resolveRead(json(detail('person-1', '最新读取人员'))));
  expect(document.querySelector('.ant-modal textarea')).toBe(textarea);
  expect(summaryRenderer.mock.lastCall?.[0].block.data).toMatchObject({ personName: '最新读取人员' });
  expect(writes).toBe(0);
});

it('keeps the real detail action confirmation and reason during locale re-reading', async () => {
  await mountDetail();
  await act(async () => button(detailMessages.PersonDetailView.Action_Disable.zhs)!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal textarea')).not.toBeNull());
  expect(document.querySelector('.ant-modal select')).toBeNull();
  const textarea = document.querySelector<HTMLTextAreaElement>('.ant-modal textarea')!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(textarea, '保留停用原因');
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await switchLanguage();
  expect(reads).toBe(2);
  expect(document.querySelector('.ant-modal textarea')).toBe(textarea);
  expect(textarea.value).toBe('保留停用原因');
  await act(async () => resolveRead(json(detail('person-1', '最新读取人员'))));
  expect(document.querySelector('.ant-modal textarea')).toBe(textarea);
  expect(textarea.value).toBe('保留停用原因');
  expect(writes).toBe(0);
});

it('does not replay an already submitting detail action when its locale snapshot changes', async () => {
  let finishAction!: () => void;
  const action = vi.fn(() => new Promise<void>(resolve => { finishAction = resolve; }));
  await mountDetail(action);
  await act(async () => button(detailMessages.PersonDetailView.Action_Disable.zhs)!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal textarea')).not.toBeNull());
  expect(document.querySelector('.ant-modal select')).toBeNull();
  await act(async () => button('确认')!.click());
  expect(action).toHaveBeenCalledTimes(1);
  await switchLanguage();
  expect(document.querySelector('.ant-modal-footer .ant-btn-loading')).not.toBeNull();
  expect(action).toHaveBeenCalledTimes(1);
  await act(async () => resolveRead(json(detail('person-1', '新语言读取'))));
  expect(action).toHaveBeenCalledTimes(1);
  await act(async () => finishAction());
  expect(action).toHaveBeenCalledTimes(1);
  expect(writes).toBe(0);
});

it.each(['denied', 'unavailable', 'record', 'permission'] as const)('does not retain an old summary across %s', async kind => {
  await openDraft();
  if (kind === 'record') await act(async () => root!.render(<Harness id="person-2" />));
  else if (kind === 'permission') await act(async () => root!.render(<Harness canBind={false} />));
  else {
    await switchLanguage();
    await act(async () => resolveRead(kind === 'denied' ? json({ code: 'Platform.Forbidden' }, 403) : json(detail('person-1', '不可用人员', false))));
  }
  expect(document.querySelector('.ant-modal textarea')).toBeNull();
  expect(document.querySelector('input[type="radio"]')).toBeNull();
  expect(writes).toBe(0);
});
