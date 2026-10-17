// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken, LocalizationProvider } from '@icp-platform/core';
import type { StandardCrudOperationResult } from '@icp-platform/sdk';
import { createObjectOwnershipActions } from '../../../ui/src/objectOwnershipActions';
import type { ObjectOwnershipInitializationTarget } from '../../../ui/src/ObjectOwnershipInitializationInput';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { platformTestCatalogs } from './localizationTestCatalogs';

// 冷转换在收集期完成；用例仍消费真实登记的 lazy 输入及完整动作生命周期。
await import('../../../ui/src/ObjectOwnershipInitializationInput');
vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const user = { id: 'administrator', ecode: 'administrator', ename: '管理员', revision: 1,
  mustChangePassword: false, isLocalAdministrator: true, featurePositions: ['ownership'], timeZoneId: 'Asia/Taipei' };
const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['location'] };
const target: ObjectOwnershipInitializationTarget = {
  pendingPath: '/api/ownership/pending', statusPath: '/api/ownership/status/{id}',
  savePath: '/api/ownership/save/{id}', accountsPath: '/api/ownership/accounts',
  eligibilityPath: '/api/ownership/eligibility', batchPath: '/api/ownership/batch', body: { position },
};
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { 'Content-Type': 'application/json' },
});
type FetchResponse = Response | Promise<Response>;
interface Responses {
  pending?: (request: Request) => FetchResponse;
  status?: (request: Request) => FetchResponse;
  accounts?: (request: Request) => FetchResponse;
  save?: (request: Request) => FetchResponse;
}
let root: Root | undefined;
let container: HTMLDivElement | undefined;
let requests: Request[] = [];
let nextUser = user;
let refreshIdentity: () => Promise<void>;
let changeTarget: (value: ObjectOwnershipInitializationTarget) => void;
let results: StandardCrudOperationResult[] = [];
const refresh = vi.fn();

afterEach(async () => {
  if (root) await act(async () => root!.unmount());
  root = undefined;
  container?.remove(); container = undefined;
  localStorage.clear(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken();
});

function installFetch(responses: Responses = {}) {
  requests = []; nextUser = user; results = []; refresh.mockReset(); invalidatePlatformAntiforgeryToken();
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'test-token' });
    if (url.pathname.endsWith('current-user')) return json(nextUser);
    if (url.pathname.endsWith('/eligibility')) return json({ allowed: true });
    if (url.pathname.endsWith('/pending')) return responses.pending?.(request) ?? json({ items: [{ id: 'one', name: '待补齐对象' }], total: 1 });
    if (url.pathname.includes('/status/')) return responses.status?.(request) ?? json({ missing: true });
    if (url.pathname.endsWith('/accounts')) return responses.accounts?.(request) ?? json([
      { accountId: 'valid', code: 'valid', name: '有部门账号', departmentId: 'department' },
      { accountId: 'unassigned', code: 'unassigned', name: '无部门账号', departmentId: null },
    ]);
    if (url.pathname.includes('/save/') || url.pathname.endsWith('/batch')) return responses.save?.(request)
      ?? json(url.pathname.endsWith('/batch') ? { changed: 1, unresolved: 0 } : { changed: true });
    throw new Error(`Unexpected ownership test request: ${request.method} ${url.pathname}`);
  }));
}

async function mount(initialTarget: ObjectOwnershipInitializationTarget = target, responses: Responses = {}) {
  installFetch(responses);
  function Consumer() {
    refreshIdentity = useAuth().retrySessionValidation;
    const [currentTarget, setTarget] = useState(initialTarget); changeTarget = setTarget;
    const registration = createObjectOwnershipActions(() => currentTarget, currentTarget.eligibilityPath);
    return <StandardCrudActions actions={[{ key: 'initializeOwnership', labelKey: '归属补齐',
      inputKey: 'initializeOwnership', confirmLabel: '确认补齐' }]}
      featureResolutionForAction={() => ({ kind: 'allowed', position: 'ownership' })}
      handlers={registration.actionHandlers} actionInputs={registration.actionInputRenderers}
      actionEligibilityResolvers={registration.actionEligibilityResolvers} context={{ id: currentTarget.initialId }}
      refresh={refresh} onResult={result => results.push(result)} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={platformTestCatalogs}>
    <AuthProvider initialUser={user}><CurrentPermissionScope><Consumer /></CurrentPermissionScope></AuthProvider>
  </LocalizationProvider></MemoryRouter>));
  await vi.waitFor(() => expect(button('归属补齐')).toBeDefined());
  await click('归属补齐');
}

function button(label: string): HTMLButtonElement | undefined {
  return [...document.querySelectorAll<HTMLButtonElement>('button')]
    .find(item => item.textContent?.replace(/\s/g, '') === label.replace(/\s/g, ''));
}
async function click(label: string, selector = 'button') {
  await act(async () => {
    const element = [...document.querySelectorAll<HTMLElement>(selector)]
      .find(item => item.textContent?.replace(/\s/g, '') === label.replace(/\s/g, ''));
    expect(element, label).toBeDefined(); element!.click();
  });
}
function writes() { return requests.filter(request => /\/save\/|\/batch$/.test(new URL(request.url).pathname)); }
async function openAccounts() {
  await click('人工指定账号', 'label');
  await act(async () => document.querySelector('[aria-label="所属人"]')!.closest('.ant-select')!
    .querySelector('.ant-select-selector')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
}
async function searchAccounts(value: string) {
  await act(async () => {
    const input = document.querySelector<HTMLInputElement>('input[aria-label="所属人"]')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

it('loads pending pages without writes and submits only the explicitly selected current page', async () => {
  const queryScope = JSON.stringify(position);
  await mount({ ...target, pendingPath: `${target.pendingPath}?queryScope=${encodeURIComponent(queryScope)}` }, { pending: request => json({ items: new URL(request.url).searchParams.get('page') === '2'
    ? [{ id: 'second', name: '第二页对象' }]
    : Array.from({ length: 20 }, (_, index) => ({ id: `first-${index + 1}`, name: `第一页对象 ${index + 1}` })), total: 21 }) });
  await vi.waitFor(() => expect(document.body.textContent).toContain('第一页对象'));
  expect(button('确认补齐')?.disabled).toBe(true);
  expect(writes()).toHaveLength(0);
  await click('选择当前许可页');
  await act(async () => document.querySelector<HTMLElement>('.ant-pagination-item-2')!.click());
  await vi.waitFor(() => expect(document.body.textContent).toContain('第二页对象'));
  await click('选择当前许可页');
  await click('确认补齐');
  expect(writes()).toHaveLength(1);
  await expect(writes()[0].clone().json()).resolves.toEqual({ position, ids: ['second'] });
  expect(refresh).toHaveBeenCalledTimes(1);
  expect(results[0]).toMatchObject({ notice: { reasonKey: 'Platform.OwnershipBatchCompleted' } });
  const pendingRequests = requests.filter(request => new URL(request.url).pathname.endsWith('/pending'));
  expect(pendingRequests.map(request => new URL(request.url).searchParams.get('page'))).toEqual(['1', '2']);
  for (const request of pendingRequests) {
    expect(request.method).toBe('GET');
    expect(request.body).toBeNull();
    expect(new URL(request.url).searchParams.get('queryScope')).toBe(queryScope);
    expect(new URL(request.url).searchParams.get('pageSize')).toBe('20');
    expect(request.headers.get('X-NewP-Feature')).toBe('ownership');
  }
});

it('searches real account candidates, disables missing departments and clears the chosen account when mode changes', async () => {
  await mount({ ...target, initialId: 'one' });
  await vi.waitFor(() => expect(document.body.textContent).toContain('按创建账号补齐'));
  await openAccounts();
  await vi.waitFor(() => expect(document.querySelector('.ant-select-item-option-disabled')?.textContent).toContain('缺少部门'));
  await act(async () => document.querySelector<HTMLElement>('.ant-select-item-option-disabled')!.click());
  expect(button('确认补齐')?.disabled).toBe(true);
  await searchAccounts('有部门');
  expect(requests.some(request => new URL(request.url).searchParams.get('query') === '有部门')).toBe(true);
  await click('valid · 有部门账号', '.ant-select-item-option-content');
  expect(button('确认补齐')?.disabled).toBe(false);
  await click('按创建账号补齐', 'label');
  await openAccounts();
  expect(button('确认补齐')?.disabled).toBe(true);
  await click('valid · 有部门账号', '.ant-select-item-option-content');
  await click('确认补齐');
  await expect(writes()[0].clone().json()).resolves.toEqual({ position, accountId: 'valid' });
});

it('keeps already complete objects non-submittable', async () => {
  const queryScope = JSON.stringify(position);
  await mount({ ...target, initialId: 'one', statusPath: `${target.statusPath}?queryScope=${encodeURIComponent(queryScope)}` },
    { status: () => json({ missing: false }) });
  await vi.waitFor(() => expect(document.body.textContent).toContain('对象归属已完整'));
  const statusRequests = requests.filter(request => new URL(request.url).pathname === '/api/ownership/status/one');
  expect(statusRequests).toHaveLength(1);
  expect(statusRequests[0].method).toBe('GET');
  expect(statusRequests[0].body).toBeNull();
  expect(new URL(statusRequests[0].url).searchParams.get('queryScope')).toBe(queryScope);
  expect(statusRequests[0].headers.get('X-NewP-Feature')).toBe('ownership');
  expect(button('确认补齐')?.disabled).toBe(true);
  await click('确认补齐');
  expect(writes()).toHaveLength(0);
});

it('blocks changes and duplicate confirmation while a selected batch is being submitted', async () => {
  let finish!: (response: Response) => void;
  await mount(target, { save: () => new Promise<Response>(resolve => { finish = resolve; }) });
  await vi.waitFor(() => expect(document.body.textContent).toContain('待补齐对象'));
  await click('选择当前许可页');
  await click('确认补齐');
  try {
    expect(writes()).toHaveLength(1);
    expect(button('选择当前许可页')?.disabled).toBe(true);
    const selection = [...document.querySelectorAll<HTMLInputElement>('.ant-table input[type="checkbox"]')];
    expect(selection.length).toBeGreaterThan(0);
    expect(selection.every(input => input.disabled)).toBe(true);
    await click('确认补齐');
    expect(writes()).toHaveLength(1);
  } finally {
    await act(async () => finish(json({ changed: 1, unresolved: 0 })));
  }
});

it('retains a manual selection after a correctable rejection and retries only after another explicit confirmation', async () => {
  let reject = true;
  await mount({ ...target, initialId: 'one' }, { save: () => reject
    ? json({ code: 'EntityOwnership.AccountUnresolved', reasonKey: 'Platform.OwnershipAccountUnresolved' }, 409)
    : json({ changed: true }) });
  await vi.waitFor(() => expect(document.body.textContent).toContain('按创建账号补齐'));
  await openAccounts();
  await click('valid · 有部门账号', '.ant-select-item-option-content');
  await click('确认补齐');
  expect(writes()).toHaveLength(1);
  expect(document.querySelector('[role="dialog"]')).not.toBeNull();
  expect(document.querySelector('.ant-select-selection-item')?.textContent).toContain('有部门账号');
  reject = false;
  await click('确认补齐');
  expect(writes()).toHaveLength(2);
  await expect(writes()[1].clone().json()).resolves.toEqual({ position, accountId: 'valid' });
});

it('aborts an obsolete status request and ignores its late result after the fixed target changes', async () => {
  let finish!: (response: Response) => void;
  await mount({ ...target, initialId: 'one' }, { status: request => new URL(request.url).pathname.endsWith('/one')
    ? new Promise<Response>(resolve => { finish = resolve; }) : json({ missing: false }) });
  await vi.waitFor(() => expect(finish).toBeTypeOf('function'));
  const old = requests.find(request => new URL(request.url).pathname.endsWith('/status/one'))!;
  await act(async () => changeTarget({ ...target, initialId: 'two' }));
  await vi.waitFor(() => expect(document.body.textContent).toContain('对象归属已完整'));
  expect(old.signal.aborted).toBe(true);
  await act(async () => finish(json({ missing: true })));
  expect(document.body.textContent).toContain('对象归属已完整');
  expect(button('确认补齐')?.disabled).toBe(true);
  expect(writes()).toHaveLength(0);
});

it('drops the old account selection and ignores a late candidate result when the signed-in account changes', async () => {
  let finish!: (response: Response) => void;
  let delay = false;
  await mount(target, { accounts: () => delay ? new Promise<Response>(resolve => { finish = resolve; })
    : json([{ accountId: 'valid', code: 'valid', name: '有部门账号', departmentId: 'department' }]) });
  await vi.waitFor(() => expect(document.body.textContent).toContain('待补齐对象'));
  await click('人工指定账号');
  await vi.waitFor(() => expect(document.querySelector('[aria-label="所属人"]')).not.toBeNull());
  await act(async () => document.querySelector('[aria-label="所属人"]')!.closest('.ant-select')!
    .querySelector('.ant-select-selector')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await click('valid · 有部门账号', '.ant-select-item-option-content');
  delay = true;
  await searchAccounts('迟到响应');
  await vi.waitFor(() => expect(finish).toBeTypeOf('function'));
  const obsolete = requests.filter(request => new URL(request.url).pathname.endsWith('/accounts')).at(-1)!;
  nextUser = { ...user, id: 'other-administrator' };
  await act(async () => refreshIdentity());
  expect(document.querySelector('[role="dialog"]')).toBeNull();
  expect(obsolete.signal.aborted).toBe(true);
  await act(async () => finish(json([{ accountId: 'obsolete', code: 'old', name: '旧账号候选', departmentId: 'old-department' }])));
  await click('归属补齐');
  await vi.waitFor(() => expect(document.body.textContent).toContain('待补齐对象'));
  expect(document.querySelector('[aria-label="所属人"]')).toBeNull();
  expect(button('确认补齐')?.disabled).toBe(true);
  expect(document.body.textContent).not.toContain('旧账号候选');
  expect(writes()).toHaveLength(0);
});

it('rejects a single-object draft whose identity no longer matches the owning page', async () => {
  installFetch();
  const registration = createObjectOwnershipActions(() => ({ ...target, initialId: 'fixed' }), target.eligibilityPath);
  await expect(registration.actionHandlers.initializeOwnership({ input: { targetId: 'another', accountId: 'valid' } }))
    .rejects.toMatchObject({ status: 409, reasonKey: 'Platform.OwnershipTargetChanged' });
  expect(requests).toHaveLength(0);
});

it.each(['ids', 'linkIds'] as const)('sends only the chosen %s batch with the owner-provided position and returns its receipt', async batchIdField => {
  installFetch({ save: () => json({ changed: 2, unresolved: 1 }) });
  const registration = createObjectOwnershipActions(() => ({ ...target, batchIdField }), target.eligibilityPath);
  const result = await registration.actionHandlers.initializeOwnership({ featurePosition: 'ownership', input: { targetIds: ['one', 'two'] } });
  await expect(writes()[0].clone().json()).resolves.toEqual({ position, [batchIdField]: ['one', 'two'] });
  expect(result).toMatchObject({ operation: 'action', payload: { changed: 2, unresolved: 1 },
    notice: { reasonKey: 'Platform.OwnershipBatchCompleted', args: { changed: 2, unresolved: 1 } } });
});

it('returns the unchanged receipt without manufacturing an object change', async () => {
  installFetch({ save: () => json({ changed: false }) });
  const registration = createObjectOwnershipActions(() => ({ ...target, initialId: 'one' }), target.eligibilityPath);
  const result = await registration.actionHandlers.initializeOwnership({ input: { targetId: 'one' } });
  expect(result).toMatchObject({ noChange: true, notice: { reasonKey: 'Platform.OwnershipComplete' } });
  expect(result).not.toHaveProperty('change');
});

it('preserves an unknown mutation outcome and never automatically replays the business request', async () => {
  installFetch({ save: () => { throw new TypeError('connection lost after send'); } });
  const registration = createObjectOwnershipActions(() => ({ ...target, initialId: 'one' }), target.eligibilityPath);
  await expect(registration.actionHandlers.initializeOwnership({ input: { targetId: 'one' } }))
    .rejects.toMatchObject({ reasonKey: 'Platform.MutationOutcomeUnknown' });
  expect(writes()).toHaveLength(1);
});
