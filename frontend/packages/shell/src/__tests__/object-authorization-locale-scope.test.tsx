// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken, LocalizationProvider, useLocalization } from '@icp-platform/core';
import { ObjectAuthorizationInput, createObjectAuthorizationActions } from '@icp-platform/ui';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { PlatformRequestLocaleBridge } from '../localization/PlatformRequestLocaleBridge';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';
import dictionary from '../../../../modules/authorization/metadata/dictionaries/DataScope.dic.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';

vi.unmock('@icp-platform/core');
const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(
  [...Object.entries(dictionary.DataScope).map(([key, value]) => [`DataScope.${key}`, value[language as 'zhs' | 'en']]),
    ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value[language as 'zhs' | 'en']])],
)]));
const user = { id: 'manager', ecode: 'manager', ename: 'Manager', revision: 1, mustChangePassword: false,
  isLocalAdministrator: false, featurePositions: ['authorize'], timeZoneId: 'Asia/Taipei' };
const config = { resourceKey: 'organization.company', objectId: 'one', subjects: [
  { id: 's', type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } },
] };
const catalog = { actions: [{ key: 'read', title: '读取', enabled: true }], subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }] };
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
let root: Root | undefined;
let container: HTMLDivElement;
let draft: unknown;
let language: (value: string) => void;
let finishCatalog: () => void;
let delay = false;
let nextUser = user;
let refreshIdentity: () => Promise<void>;
let changeTarget: (value: string) => void;
let savedOpinion = 'unset';
let catalogFailure = 0;
let requests: { path: string; locale: string | null; method: string; body?: string }[];
afterEach(async () => { if (root) await act(async () => root?.unmount()); root = undefined; container?.remove();
  localStorage.clear(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); });

async function mount(inspect = false) {
  delay = false; requests = []; nextUser = user; savedOpinion = 'unset'; catalogFailure = 0;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname + new URL(request.url).search;
    requests.push({ path, locale: request.headers.get('X-ICP-Locale'), method: request.method,
      body: request.method === 'PUT' ? await request.clone().text() : undefined });
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(nextUser);
    if (request.method === 'PUT') return new Response(null, { status: 204 });
    if (path.endsWith('/catalog')) {
      if (catalogFailure) return new Response('{}', { status: catalogFailure });
      if (delay) return new Promise<Response>(resolve => { finishCatalog = () => resolve(json(catalog)); });
      return json(catalog);
    }
    if (path.includes('/subjects/')) return json({ items: [{ id: 'second', name: 'Second' }], currentValues: [], hasMore: true });
    if (path.includes('/explanation')) return json({ accountId: path.includes('second') ? 'second' : 'manager',
      accountName: request.headers.get('X-ICP-Locale') === 'en-US' ? 'Fresh display' : '旧显示',
      actions: [{ key: 'read', title: '读取', allowed: true, reason: 'default', sources: [] }] });
    return json({ ...config, objectId: path.split('/').at(-1), subjects: [{ ...config.subjects[0], actions: { read: savedOpinion } }] });
  }));
  function Input() {
    const { setLanguage } = useLocalization(); language = setLanguage;
    refreshIdentity = useAuth().retrySessionValidation;
    const [value, setValue] = useState<unknown>(); draft = value;
    const [target, setTarget] = useState('one'); changeTarget = setTarget;
    const [open, setOpen] = useState(true);
    return open ? <StandardCrudActionReviewDialog title="Object authorization" pending={false}
      action={{ key: 'authorize', labelKey: 'Authorize', confirmLabel: 'Save once', inputKey: inspect ? 'objectAuthorizationDetails' : 'objectAuthorization' }}
      inputRenderer={context => <ObjectAuthorizationInput {...context} target={{ resourceKey: 'organization.company', id: target, title: 'Company' }} />}
      featurePosition="authorize" value={value} onChange={setValue} onCancel={() => { setOpen(false); setValue(undefined); }}
      onConfirm={() => { void createObjectAuthorizationActions('organization.company', () => ({ title: 'Company' })).actionHandlers.authorize({ id: target, featurePosition: 'authorize', input: value }); }} /> : null;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider initialLanguage="zhs" defaultLanguage="zhs" catalogs={catalogs}
    supportedLanguages={[{ code: 'zhs', displayName: '中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]}>
    <PlatformRequestLocaleBridge><AuthProvider initialUser={user}><CurrentPermissionScope><Input /></CurrentPermissionScope></AuthProvider></PlatformRequestLocaleBridge>
  </LocalizationProvider></MemoryRouter>));
}
const click = async (text: string, selector = 'button') => act(async () => {
  const element = [...document.querySelectorAll(selector)].find(item => item.textContent?.replace(/\s/g, '') === text.replace(/\s/g, '')) as HTMLElement;
  expect(element, text).toBeDefined(); element.click();
});

it('accepts authoritative saved changes when pristine and drops interactions when the target changes', async () => {
  await mount();
  savedOpinion = 'deny';
  await act(async () => language('en'));
  expect(draft).toMatchObject({ subjects: [{ actions: { read: 'deny' } }] });
  await click('Edit');
  delay = true;
  await act(async () => language('zhs'));
  const obsolete = finishCatalog;
  await act(async () => changeTarget('two'));
  expect(document.querySelector('.ant-drawer')).toBeNull();
  expect([...document.querySelectorAll('button')].find(button => button.textContent === 'Save once')?.disabled).toBe(true);
  await act(async () => obsolete());
  expect(document.querySelector('.ant-drawer')).toBeNull();
  await act(async () => { delay = false; finishCatalog(); });
  expect(draft).toMatchObject({ objectId: 'two' });
  expect(requests.filter(request => request.method === 'PUT')).toHaveLength(0);
});

it('removes old authorization input when revalidation explicitly denies access', async () => {
  await mount();
  await click('编辑');
  catalogFailure = 403;
  await act(async () => language('en'));
  expect(document.querySelector('.ant-drawer')).toBeNull();
  expect(document.querySelector('[role="checkbox"]')).toBeNull();
  expect(draft).toBeUndefined();
  expect(requests.filter(request => request.method === 'PUT')).toHaveLength(0);
});

it('keeps the independent draft and open subject edits after a transient locale read failure without permitting writes', async () => {
  await mount();
  await act(async () => document.querySelector<HTMLElement>('[role="checkbox"]')!.click());
  await click('编辑');
  const drawer = document.querySelector('.ant-drawer');
  await act(async () => drawer!.querySelector<HTMLElement>('[role="checkbox"]')!.click());
  catalogFailure = 503;
  await act(async () => language('en'));
  expect(document.querySelector('.ant-drawer')).toBe(drawer);
  expect(draft).toMatchObject({ subjects: [{ actions: { read: 'allow' } }] });
  expect(drawer!.querySelector('[role="checkbox"]')?.getAttribute('aria-label')).toBe('Read: Deny');
  expect(drawer!.querySelector<HTMLButtonElement>('[role="checkbox"]')?.disabled).toBe(true);
  const save = [...document.querySelectorAll('button')].find(button => button.textContent === 'Save once')!;
  expect(save.disabled).toBe(true);
  await act(async () => save.click());
  expect(requests.filter(request => request.method === 'PUT')).toHaveLength(0);
  catalogFailure = 0;
  await click('Retry');
  expect(document.querySelector('.ant-drawer')).toBe(drawer);
  await click('Confirm', '.ant-drawer button');
  await click('Save once');
  expect(requests.filter(request => request.method === 'PUT')).toHaveLength(1);
  expect(draft).toMatchObject({ subjects: [{ actions: { read: 'deny' } }] });
});

it('retains analysis context after a transient catalog failure but hides stale conclusions until revalidated', async () => {
  await mount(true);
  const select = document.querySelector('[aria-label="分析用户"]')!.closest('.ant-select')!.querySelector('.ant-select-selector')!;
  await act(async () => select.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await click('Second', '.ant-select-item-option-content');
  await click('下一页用户');
  await act(async () => document.querySelector<HTMLElement>('.ant-collapse-header')!.click());
  catalogFailure = 503;
  await act(async () => language('en'));
  expect(document.querySelector('[aria-label="Analyze user"]')).not.toBeNull();
  expect(document.querySelector<HTMLInputElement>('input[aria-label="Analyze user"]')?.disabled).toBe(true);
  expect(document.querySelector('.ant-collapse')).toBeNull();
  catalogFailure = 0;
  delay = true;
  await click('Retry');
  expect(document.querySelector('.ant-collapse')).toBeNull();
  expect(document.querySelector<HTMLInputElement>('input[aria-label="Analyze user"]')?.disabled).toBe(true);
  await act(async () => { delay = false; finishCatalog(); });
  expect(document.querySelector('.ant-collapse-header')?.getAttribute('aria-expanded')).toBe('true');
  expect(document.body.textContent).toContain('Fresh display');
  expect(requests.some(request => request.locale === 'en-US' && request.path.includes('accountId=second'))).toBe(true);
  expect(requests.some(request => request.locale === 'en-US' && request.path.includes('page=2'))).toBe(true);
  expect(requests.filter(request => request.method === 'PUT')).toHaveLength(0);
});

it('drops the previous subject interaction and ignores its late locale response after authentication changes', async () => {
  await mount();
  await click('编辑');
  delay = true;
  await act(async () => language('en'));
  const oldLocaleResult = finishCatalog;
  nextUser = { ...user, id: 'other-manager' };
  await act(async () => refreshIdentity());
  expect(document.querySelector('.ant-drawer')).toBeNull();
  expect(document.querySelector('[role="dialog"]')).toBeNull();
  await act(async () => oldLocaleResult());
  expect(document.querySelector('.ant-drawer')).toBeNull();
  expect(document.querySelector('[role="dialog"]')).toBeNull();
  expect(draft).toBeUndefined();
  expect(requests.filter(request => request.method === 'PUT')).toHaveLength(0);
});

it('preserves the object draft and open subject edits through the real locale query scope and saves once', async () => {
  await mount();
  await act(async () => document.querySelector<HTMLElement>('[role="checkbox"]')!.click());
  await click('编辑');
  const drawer = document.querySelector('.ant-drawer');
  await act(async () => drawer!.querySelector<HTMLElement>('[role="checkbox"]')!.click());
  delay = true;
  await act(async () => language('en'));
  expect(document.querySelector('.ant-drawer')).toBe(drawer);
  expect(draft).toMatchObject({ subjects: [{ actions: { read: 'allow' } }] });
  await act(async () => { delay = false; finishCatalog(); });
  expect(document.querySelector('.ant-drawer')).toBe(drawer);
  expect(drawer!.querySelector('[role="checkbox"]')?.getAttribute('aria-label')).toBe('Read: Deny');
  await click('Confirm', '.ant-drawer button');
  await click('Save once');
  const writes = requests.filter(request => request.method === 'PUT');
  expect(writes).toHaveLength(1);
  expect(JSON.parse(writes[0].body!)).toMatchObject({ subjects: [{ actions: { read: 'deny' } }] });
  expect(requests.some(request => request.path.endsWith('/catalog') && request.locale === 'en-US')).toBe(true);
});

it('preserves the analyzed account, directory page and expanded explanation while refreshed display arrives', async () => {
  await mount(true);
  const select = document.querySelector('[aria-label="分析用户"]')!.closest('.ant-select')!.querySelector('.ant-select-selector')!;
  await act(async () => select.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await click('Second', '.ant-select-item-option-content');
  await click('下一页用户');
  await act(async () => document.querySelector<HTMLElement>('.ant-collapse-header')!.click());
  delay = true;
  await act(async () => language('en'));
  expect(document.querySelector('.ant-collapse-header')?.getAttribute('aria-expanded')).toBe('true');
  await act(async () => { delay = false; finishCatalog(); });
  expect(document.querySelector('.ant-collapse-header')?.getAttribute('aria-expanded')).toBe('true');
  expect(document.body.textContent).toContain('Fresh display');
  expect(requests.some(request => request.locale === 'en-US' && request.path.includes('accountId=second'))).toBe(true);
  expect(requests.some(request => request.locale === 'en-US' && request.path.includes('page=2'))).toBe(true);
});
