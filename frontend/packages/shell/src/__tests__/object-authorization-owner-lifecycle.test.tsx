// @vitest-environment jsdom
import { act, useEffect, useMemo, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken, LocalizationProvider } from '@icp-platform/core';
import { createObjectAuthorizationActions } from '../../../ui/src/objectAuthorizationActions';
import type { SubjectType } from '../../../ui/src/DataScopeSubjectEditor';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';
import dictionary from '../../../../modules/platform/authorization/metadata/dictionaries/DataScope.dic.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';

// 收集期完成真实 lazy 输入转换，交互预算只包含本用例的请求与页面生命周期。
await import('../../../ui/src/ObjectAuthorizationInput');
vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const user = { id: 'reader', ecode: 'reader', ename: '当前读者', revision: 1, mustChangePassword: false,
  isLocalAdministrator: false, featurePositions: ['query', 'manage'], timeZoneId: 'Asia/Taipei' };
const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['container'] };
const basePath = `/api/workspace/content/occurrence/permissions?queryScope=${encodeURIComponent(JSON.stringify(position))}`;
const catalogs = { zhs: Object.fromEntries([
  ...Object.entries(dictionary.DataScope).map(([key, value]) => [`DataScope.${key}`, value.zhs]),
  ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]),
]) };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { 'Content-Type': 'application/json' },
});
let root: Root | undefined;
let container: HTMLDivElement | undefined;
const disposeInstances: Array<() => void> = [];

afterEach(async () => {
  if (root) await act(async () => root!.unmount());
  root = undefined;
  for (const dispose of disposeInstances.splice(0)) dispose();
  container?.remove(); container = undefined;
  localStorage.clear(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken();
});

async function mountEditor(save?: () => Promise<Response>, settings: { readCurrent?: () => Response; denyAfterSave?: boolean; failRefresh?: boolean; subjectTypes?: SubjectType[] } = {}) {
  const instance = new ActionInstanceScope(); disposeInstances.push(instance.activate());
  const requests: Request[] = [];
  const saves: Array<{ subjects: Array<{ expiresAt?: string | null }> }> = [];
  let configurationReads = 0;
  let subjects = [{ id: 'direct', type: 'all', subjectId: null, priority: 0, actions: { read: 'allow' }, expiresAt: '2027-03-04T05:06:07Z' as string | null }];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.endsWith('/eligibility')) return settings.denyAfterSave && saves.length ? json({}, 403) : json({ allowed: true });
    if (url.pathname.endsWith('/catalog')) return json({ actions: [{ key: 'read', title: '读取', enabled: true }],
      subjectTypes: settings.subjectTypes ?? [{ key: 'all', title: '所有用户', requiresId: false }] });
    if (request.method === 'PUT') {
      const config = await request.clone().json() as { subjects: typeof subjects };
      saves.push(config); subjects = config.subjects;
      if (save) return save();
      return new Response(null, { status: 204 });
    }
    if (url.pathname === '/api/workspace/content/occurrence/permissions') {
      configurationReads++;
      return configurationReads > 1 && settings.readCurrent ? settings.readCurrent() : json({ resourceKey: 'workspace.folder', objectId: 'object', subjects });
    }
    throw new Error(`Unexpected editor request: ${request.method} ${url.pathname}`);
  }));
  const registration = createObjectAuthorizationActions('workspace.folder', () => ({ id: 'object', title: '文件夹', name: '目标', basePath }));
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><CurrentPermissionScope><StandardCrudActions
      actions={[{ key: 'authorize', labelKey: '维护授权', inputKey: 'objectAuthorization', confirmLabel: '保存授权' }]}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'manage' })}
      handlers={registration.actionHandlers} actionInputs={registration.actionInputRenderers}
      actionEligibilityResolvers={registration.actionEligibilityResolvers} context={{ id: 'object', instance }}
      refresh={vi.fn(() => { if (settings.failRefresh) throw new Error('Read rejected after permission was revoked'); })} />
    </CurrentPermissionScope></AuthProvider>
  </LocalizationProvider></MemoryRouter>));
  await vi.waitFor(() => expect(button('维护授权')).toBeDefined());
  await act(async () => button('维护授权')!.click());
  await vi.waitFor(() => expect(document.querySelector('tr[data-row-key="direct"]')).not.toBeNull());
  return { requests, saves };
}

function button(label: string): HTMLButtonElement | undefined {
  return [...document.querySelectorAll<HTMLButtonElement>('button')]
    .find(element => element.textContent?.replace(/\s/g, '') === label);
}

it('lets a browsing user analyze another account through the exact owner source when authorization management is denied', async () => {
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.endsWith('/eligibility')) return json({ code: 'Authorization.ObjectForbidden' }, 403);
    if (url.pathname === '/api/workspace/content/occurrence/permissions/catalog') return json({
      actions: [{ key: 'read', title: '读取', enabled: true }],
      subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }, { key: 'user', title: '账号', requiresId: true }],
    });
    if (url.pathname === '/api/workspace/content/occurrence/permissions/subjects/user') return json({
      items: [{ id: 'other', name: '另一个账号' }], currentValues: [], hasMore: false,
    });
    if (url.pathname === '/api/workspace/content/occurrence/permissions/explanation') return json({
      accountId: url.searchParams.get('accountId') ?? user.id,
      accountName: url.searchParams.has('accountId') ? '另一个账号' : user.ename,
      actions: [{ key: 'read', title: '读取', allowed: true, reason: 'default', sources: [] }],
    });
    throw new Error(`Unexpected authorization request: ${request.method} ${url.pathname}`);
  }));
  const registration = createObjectAuthorizationActions('workspace.folder', () => ({
    id: 'object', title: '文件夹', name: '可浏览文件夹', basePath,
  }));
  function CurrentIdentity() { return <span data-current-account>{useAuth().user?.id}</span>; }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><CurrentPermissionScope><CurrentIdentity /><StandardCrudActions
      actions={[
        { key: 'viewAuthorization', labelKey: '查询权限', handlerKey: 'authorize', inputKey: 'objectAuthorizationDetails' },
        { key: 'authorize', labelKey: '维护授权', inputKey: 'objectAuthorization', confirmLabel: '保存授权' },
      ]}
      featureResolutionForAction={action => ({ kind: 'allowed', position: action.key === 'authorize' ? 'manage' : 'query' })}
      handlers={registration.actionHandlers} actionInputs={registration.actionInputRenderers}
      actionEligibilityResolvers={registration.actionEligibilityResolvers} context={{ id: 'object' }} refresh={vi.fn()} />
    </CurrentPermissionScope></AuthProvider>
  </LocalizationProvider></MemoryRouter>));

  await vi.waitFor(() => expect(button('查询权限')).toBeDefined());
  expect(button('维护授权')).toBeUndefined();
  await act(async () => button('查询权限')!.click());
  await vi.waitFor(() => expect(document.querySelector('input[aria-label="分析用户"]')).not.toBeNull());
  await act(async () => document.querySelector('input[aria-label="分析用户"]')!.closest('.ant-select')!
    .querySelector('.ant-select-selector')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect([...document.querySelectorAll('.ant-select-item-option-content')]
    .some(option => option.textContent === '另一个账号')).toBe(true));
  await act(async () => [...document.querySelectorAll<HTMLElement>('.ant-select-item-option-content')]
    .find(option => option.textContent === '另一个账号')!.click());
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).searchParams.get('accountId') === 'other')).toBe(true));

  expect(document.querySelector('[data-current-account]')?.textContent).toBe(user.id);
  expect(requests.filter(request => request.method !== 'GET')).toHaveLength(0);
  const ownerRequests = requests.filter(request => new URL(request.url).pathname.startsWith('/api/workspace/content/occurrence/permissions'));
  expect(ownerRequests.length).toBeGreaterThan(0);
  for (const request of ownerRequests) {
    const url = new URL(request.url);
    expect(url.searchParams.get('queryScope')).toBe(JSON.stringify(position));
    expect(request.body).toBeNull();
    expect(request.headers.get('X-NewP-Feature')).toBe(url.pathname.endsWith('/eligibility') ? 'manage' : 'query');
    expect(url.pathname).not.toBe('/api/workspace/content/occurrence/permissions');
  }
  expect(requests.filter(request => new URL(request.url).pathname.includes('/api/authorization/data-scope/objects/'))).toHaveLength(0);
});

it('edits a direct expiry using the existing UTC second picker and clears it to permanent without changing other grant fields', async () => {
  const { saves } = await mountEditor();
  const edit = async () => {
    await act(async () => document.querySelector<HTMLElement>('tr[data-row-key="direct"]')!
      .querySelectorAll<HTMLButtonElement>('button').forEach(element => { if (element.textContent?.replace(/\s/g, '') === '编辑') element.click(); }));
    await vi.waitFor(() => expect(document.querySelector<HTMLInputElement>('input[id="expiresAt"]')).not.toBeNull());
    return document.querySelector<HTMLInputElement>('input[id="expiresAt"]')!;
  };
  let input = await edit();
  expect(input.value).toBe('2027-03-04 05:06:07');
  expect(document.querySelector('label[for="expiresAt"]')?.textContent).toContain('UTC');
  await act(async () => input.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-picker-dropdown:not(.ant-picker-dropdown-hidden)')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('.ant-picker-dropdown:not(.ant-picker-dropdown-hidden) td[title="2027-03-05"] .ant-picker-cell-inner')!.click());
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-picker-dropdown:not(.ant-picker-dropdown-hidden) .ant-picker-ok button')!.click());
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('.ant-drawer-open button')]
    .find(element => element.textContent?.replace(/\s/g, '') === '确定')!.click());
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(1);
  expect(saves[0].subjects).toEqual([{ id: 'direct', type: 'all', subjectId: null, priority: 0,
    actions: { read: 'allow' }, expiresAt: '2027-03-05T05:06:07Z' }]);
  await act(async () => button('维护授权')!.click());
  await vi.waitFor(() => expect(document.querySelector('tr[data-row-key="direct"]')).not.toBeNull());
  input = await edit();
  expect(input.value).toBe('2027-03-05 05:06:07');
  await act(async () => input.closest('.ant-picker')!.querySelector<HTMLElement>('.ant-picker-clear')!.click());
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('.ant-drawer-open button')]
    .find(element => element.textContent?.replace(/\s/g, '') === '确定')!.click());
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(2);
  expect(saves[1].subjects[0].expiresAt).toBeNull();
});

it('freezes subject and expiry editing and prevents duplicate confirmation while a real save is pending', async () => {
  let finish!: (response: Response) => void;
  const { saves } = await mountEditor(() => new Promise<Response>(resolve => { finish = resolve; }));
  await act(async () => button('保存授权')!.click());
  try {
    await vi.waitFor(() => expect(saves).toHaveLength(1));
    expect(button('添加主体项')?.disabled).toBe(true);
    const rowButtons = [...document.querySelectorAll<HTMLButtonElement>('tr[data-row-key="direct"] button')];
    expect(rowButtons.length).toBeGreaterThan(0);
    expect(rowButtons.every(element => element.disabled)).toBe(true);
    expect(document.querySelector<HTMLButtonElement>('tr[data-row-key="direct"] button[role="checkbox"]')?.disabled).toBe(true);
    await act(async () => button('保存授权')!.click());
    expect(saves).toHaveLength(1);
  } finally { await act(async () => finish(new Response(null, { status: 204 }))); }
});

it('freezes an unknown save outcome and offers an explicit read-only reconciliation without replaying the write', async () => {
  const { requests, saves } = await mountEditor(async () => { throw new TypeError('Response lost after commit'); });
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(1);
  expect(button('保存授权')?.disabled).toBe(true);
  expect(button('核对结果')).toBeDefined();
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(1);
  await act(async () => button('取消')!.click());
  await act(async () => button('维护授权')!.click());
  await vi.waitFor(() => expect(button('核对结果')).toBeDefined());
  expect(button('保存授权')?.disabled).toBe(true);
  const configurationReads = () => requests.filter(request => request.method === 'GET'
    && new URL(request.url).pathname === '/api/workspace/content/occurrence/permissions').length;
  expect(configurationReads()).toBe(1);
  await act(async () => button('核对结果')!.click());
  await vi.waitFor(() => expect(document.body.textContent).toContain('已读取当前配置'));
  expect(configurationReads()).toBe(2);
  expect(button('保存授权')?.disabled).toBe(true);
  expect(saves).toHaveLength(1);
});

it.each(['denied', 'fault', 'different'])('keeps the unknown write frozen after a %s reconciliation result', async outcome => {
  const { requests, saves } = await mountEditor(async () => { throw new TypeError('Response lost'); }, {
    readCurrent: () => outcome === 'different' ? json({ resourceKey: 'workspace.folder', objectId: 'object', subjects: [
      { id: 'current', type: 'all', subjectId: null, priority: 0, actions: { read: 'deny' }, expiresAt: null },
    ] }) : json({}, outcome === 'denied' ? 403 : 500),
  });
  await act(async () => button('保存授权')!.click());
  await act(async () => button('核对结果')!.click());
  await vi.waitFor(() => expect(button('核对结果')?.classList.contains('ant-btn-loading')).toBe(false));
  expect(requests.filter(request => request.method === 'GET'
    && new URL(request.url).pathname === '/api/workspace/content/occurrence/permissions')).toHaveLength(2);
  if (outcome === 'different') {
    expect(document.body.textContent).toContain('不能确定原保存请求的提交结果');
    expect(document.querySelector('tr[data-row-key="current"] button[role="checkbox"]')?.getAttribute('aria-checked')).toBe('mixed');
  } else {
    expect(document.body.textContent).not.toContain('已读取当前配置');
    if (outcome === 'denied') expect(document.querySelector('tr[data-row-key="direct"]')).toBeNull();
  }
  expect(button('保存授权')?.disabled).toBe(true);
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(1);
});

it('preserves a real 204 save after self-revocation and a failed detail refresh without reading sensitive configuration again', async () => {
  const { requests, saves } = await mountEditor(undefined, { denyAfterSave: true, failRefresh: true });
  await act(async () => button('保存授权')!.click());
  await vi.waitFor(() => expect(button('维护授权')).toBeUndefined());
  expect(saves).toHaveLength(1);
  expect(document.body.textContent).toContain('已保存，刷新失败');
  expect(document.querySelector('[role="dialog"]')).toBeNull();
  expect(requests.filter(request => request.method === 'GET'
    && new URL(request.url).pathname === '/api/workspace/content/occurrence/permissions')).toHaveLength(1);
  expect(requests.some(request => new URL(request.url).pathname.endsWith('/explanation'))).toBe(false);
});

it('retains the editable direct grant after a validation rejection and retries only on another explicit save', async () => {
  let reject = true;
  const { saves } = await mountEditor(async () => reject ? json({ reasonKey: 'DataScope.ObjectNotLoaded' }, 400) : new Response(null, { status: 204 }));
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(1);
  expect(document.querySelector('tr[data-row-key="direct"]')).not.toBeNull();
  expect(document.body.textContent).toContain('2027-03-04 05:06:07 UTC');
  expect(button('保存授权')?.disabled).toBe(false);
  expect(button('核对结果')).toBeUndefined();
  reject = false;
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(2);
  expect(document.querySelector('[role="dialog"]')).toBeNull();
});

it.each(['source', 'account'])('aborts the previous configuration and discards its late response after the %s boundary changes', async boundary => {
  let nextUser = user;
  let refreshIdentity!: () => Promise<void>;
  let changeSource!: () => void;
  let finishOld!: (response: Response) => void;
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(nextUser);
    if (url.pathname.endsWith('/eligibility')) return json({ allowed: true });
    if (url.pathname.endsWith('/catalog')) return json({ actions: [{ key: 'read', title: '读取', enabled: true }],
      subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }] });
    if (url.pathname.endsWith('/permissions')) {
      if (nextUser.id === user.id && url.pathname.includes('/first/')) return new Promise<Response>(resolve => { finishOld = resolve; });
      return json({ resourceKey: 'workspace.folder', objectId: 'object', subjects: [
        { id: 'fresh', type: 'all', subjectId: null, priority: 0, actions: { read: 'allow' }, expiresAt: null },
      ] });
    }
    throw new Error(`Unexpected lifecycle request: ${url.pathname}`);
  }));
  function Consumer() {
    const auth = useAuth(); refreshIdentity = auth.retrySessionValidation;
    const instance = useMemo(() => new ActionInstanceScope(), []);
    useEffect(() => instance.activate(), [instance]);
    const [source, setSource] = useState('first'); changeSource = () => setSource('second');
    const currentPosition = { ...position, linkIds: [source + '-container'] };
    const sourcePath = `/api/workspace/content/${source}/permissions?queryScope=${encodeURIComponent(JSON.stringify(currentPosition))}`;
    const registration = createObjectAuthorizationActions('workspace.folder', () => ({ id: 'object', title: '文件夹', basePath: sourcePath }));
    return <StandardCrudActions actions={[{ key: 'authorize', labelKey: '维护授权', inputKey: 'objectAuthorization', confirmLabel: '保存授权' }]}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'manage' })}
      handlers={registration.actionHandlers} actionInputs={registration.actionInputRenderers}
      actionEligibilityResolvers={registration.actionEligibilityResolvers} refresh={vi.fn()}
      context={{ id: 'object', instance, queryScope: currentPosition }} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><CurrentPermissionScope><Consumer /></CurrentPermissionScope></AuthProvider>
  </LocalizationProvider></MemoryRouter>));
  await vi.waitFor(() => expect(button('维护授权')).toBeDefined());
  await act(async () => button('维护授权')!.click());
  await vi.waitFor(() => expect(finishOld).toBeTypeOf('function'));
  const oldRequest = requests.find(request => new URL(request.url).pathname === '/api/workspace/content/first/permissions')!;
  await act(async () => {
    if (boundary === 'source') changeSource();
    else { nextUser = { ...user, id: 'other-actor', ecode: 'other-actor' }; await refreshIdentity(); }
  });
  await vi.waitFor(() => expect(oldRequest.signal.aborted).toBe(true));
  await act(async () => finishOld(json({ resourceKey: 'workspace.folder', objectId: 'object', subjects: [
    { id: 'old-private', type: 'all', subjectId: null, priority: 0, actions: { read: 'deny' }, expiresAt: null },
  ] })));
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  await vi.waitFor(() => expect(button('维护授权')).toBeDefined());
  await act(async () => button('维护授权')!.click());
  await vi.waitFor(() => expect(document.querySelector('tr[data-row-key="fresh"]')).not.toBeNull());
  expect(document.querySelector('tr[data-row-key="old-private"]')).toBeNull();
  expect(requests.some(request => request.method === 'PUT')).toBe(false);
});

it('revalidates the exact occurrence when the same EID moves to another LID and complete position', async () => {
  const requests: Request[] = [];
  let changeSource!: () => void;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname === '/api/workspace/content/first/permissions/eligibility') return json({ allowed: true });
    if (url.pathname === '/api/workspace/content/second/permissions/eligibility') return json({}, 403);
    throw new Error(`Unexpected source qualification: ${url.pathname}`);
  }));
  function Consumer() {
    const [occurrence, setOccurrence] = useState('first');
    changeSource = () => setOccurrence('second');
    const sourcePosition = { ...position, linkIds: [occurrence + '-container'] };
    const registration = createObjectAuthorizationActions('workspace.folder', context => ({ id: 'object', title: '文件夹',
      basePath: (context.row as { basePath: string }).basePath }));
    return <StandardCrudActions actions={[{ key: 'authorize', labelKey: '维护授权', inputKey: 'objectAuthorization' }]}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'manage' })}
      handlers={registration.actionHandlers} actionInputs={registration.actionInputRenderers}
      actionEligibilityResolvers={registration.actionEligibilityResolvers} refresh={vi.fn()}
      context={{ id: 'object', queryScope: sourcePosition,
        row: { basePath: `/api/workspace/content/${occurrence}/permissions?queryScope=${encodeURIComponent(JSON.stringify(sourcePosition))}` } }} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><CurrentPermissionScope><Consumer /></CurrentPermissionScope></AuthProvider>
  </LocalizationProvider></MemoryRouter>));
  await vi.waitFor(() => expect(button('维护授权')).toBeDefined());
  await act(async () => changeSource());
  await vi.waitFor(() => expect(button('维护授权')).toBeUndefined());
  const second = requests.filter(request => new URL(request.url).pathname === '/api/workspace/content/second/permissions/eligibility');
  expect(second).toHaveLength(1);
  expect(new URL(second[0].url).searchParams.get('queryScope')).toBe(JSON.stringify({ ...position, linkIds: ['second-container'] }));
});

it.each([
  { key: 'creator', title: '创建人', requiresId: false, supportsDescendants: false },
  { key: 'owner', title: '所属人', requiresId: false, supportsDescendants: false },
  { key: 'ownerDepartment', title: '所属部门', requiresId: false, supportsDescendants: true },
])('uses the catalog driven dynamic subject $key without an account selector', async type => {
  const { requests, saves } = await mountEditor(undefined, { subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }, type] });
  await act(async () => button('编辑')!.click());
  const typeSelector = document.querySelector('[aria-label="主体类型"]')!.closest('.ant-select')!.querySelector('.ant-select-selector')!;
  await act(async () => typeSelector.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  const dynamicOption = [...document.querySelectorAll<HTMLElement>('.ant-select-item-option-content')].find(option => option.textContent === type.title);
  expect(dynamicOption).toBeDefined();
  await act(async () => dynamicOption!.click());
  expect(document.querySelector('[aria-label="主体选择"]')).toBeNull();
  const descendants = document.querySelector<HTMLButtonElement>('[aria-label="包含下级部门"]');
  if (type.supportsDescendants) {
    expect(descendants?.getAttribute('aria-checked')).toBe('false');
    await act(async () => descendants!.click());
    expect(descendants!.getAttribute('aria-checked')).toBe('true');
  } else expect(descendants).toBeNull();
  await act(async () => button('确定')!.click());
  await act(async () => button('保存授权')!.click());
  expect(saves).toHaveLength(1);
  expect(saves[0].subjects[0]).toMatchObject({ type: type.key, subjectId: null,
    ...(type.supportsDescendants ? { matchOptions: { includeDescendants: true } } : {}) });
  expect(requests.some(request => new URL(request.url).pathname.includes('/subjects/'))).toBe(false);
});
