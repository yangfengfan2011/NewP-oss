// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken, LocalizationProvider } from '@icp-platform/core';
import ObjectOwnershipTransferInput, { ownershipTransferSource } from '../../../ui/src/ObjectOwnershipTransferInput';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import type { ActionDataAccessRequirement } from '@icp-platform/sdk';
import declarations from '../../../../../tests/Kernel.Tests/Fixtures/OwnershipDataAccess.contract.json';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
let root: Root | undefined;
let container: HTMLDivElement | undefined;
const instance = new ActionInstanceScope();
let deactivate: (() => void) | undefined;
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
afterEach(async () => {
  if (root) await act(async () => root!.unmount());
  root = undefined; container?.remove(); container = undefined; deactivate?.();
  vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken();
});

it('selects an existing recipient without a department and preserves the complete selection source in read requests', async () => {
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    if (new URL(request.url).pathname.endsWith('/eligibility')) return json({ allowed: true });
    if (new URL(request.url).pathname.endsWith('/accounts')) return json([{ accountId: 'disabled-recipient', code: 'recipient', name: '无部门接收账号', departmentId: null }]);
    throw new Error(`Unexpected transfer request ${request.method} ${request.url}`);
  }));
  deactivate = instance.activate();
  const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['container'] };
  const query = new URLSearchParams({ queryScope: JSON.stringify(position), selectedIds: JSON.stringify(['link-a', 'link-b']) });
  const path = '/api/workspace/ownership/transfer';
  let draft: unknown;
  let state: { dirty: boolean; ready: boolean } | undefined;
  function Harness() {
    const [value, setValue] = useState<unknown>();
    return <ObjectOwnershipTransferInput action={{ key: 'transfer', labelKey: '转交' }} instance={instance} featurePosition="actual-transfer-position"
      target={{ savePath: path, eligibilityPath: `${path}/eligibility?${query}`, accountsPath: `${path}/accounts?${query}`, body: { position, linkIds: ['link-a', 'link-b'] } }}
      value={value} onChange={next => { draft = next; setValue(next); }} onDraftStateChange={next => { state = next; }} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><Harness /></LocalizationProvider>));
  await vi.waitFor(() => expect(state?.ready).toBe(false));
  const select = document.querySelector('[role="combobox"]')!;
  await act(async () => { select.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })); });
  await vi.waitFor(() => expect(document.querySelector('[title="无部门接收账号 (recipient)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="无部门接收账号 (recipient)"]')!.click());
  await vi.waitFor(() => expect(state?.ready).toBe(true));
  expect(draft).toMatchObject({ recipientId: 'disabled-recipient' });
  for (const request of requests) {
    expect(request.method).toBe('GET'); expect(request.body).toBeNull();
    const url = new URL(request.url);
    expect(JSON.parse(url.searchParams.get('queryScope')!)).toEqual(position);
    expect(JSON.parse(url.searchParams.get('selectedIds')!)).toEqual(['link-a', 'link-b']);
    expect(request.headers.get('X-NewP-Feature')).toBe('actual-transfer-position');
  }
});

it('freezes the selected recipient when the submitted outcome is unknown and does not turn a read check into a new submission', async () => {
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init); requests.push(request);
    return new URL(request.url).pathname.endsWith('/eligibility') ? json({ allowed: true })
      : json([{ accountId: 'recipient', code: 'receiver', name: '接收人', departmentId: null }]);
  }));
  deactivate = instance.activate();
  let state: { dirty: boolean; ready: boolean } | undefined;
  let fail: () => void = () => {};
  function Harness() {
    const [value, setValue] = useState<unknown>(); const [failure, setFailure] = useState<unknown>();
    fail = () => setFailure(new Error('connection ended after send'));
    return <ObjectOwnershipTransferInput action={{ key: 'transfer', labelKey: '转交' }} instance={instance} featurePosition="transfer"
      target={{ savePath: '/transfer', eligibilityPath: '/transfer/eligibility', accountsPath: '/transfer/accounts' }}
      value={value} onChange={setValue} submissionFailure={failure} onDraftStateChange={next => { state = next; }} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><Harness /></LocalizationProvider>));
  await vi.waitFor(() => expect(document.querySelector('.ant-select-disabled')).toBeNull());
  await act(async () => document.querySelector('[role="combobox"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.querySelector('[title="接收人 (receiver)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="接收人 (receiver)"]')!.click());
  await vi.waitFor(() => expect(state?.ready).toBe(true));
  await act(async () => fail());
  await vi.waitFor(() => expect(state?.ready).toBe(false));
  expect(document.querySelector('.ant-select-disabled')).not.toBeNull();
  expect(document.body.textContent).toContain('转交结果未知');
  expect(requests.every(request => request.method === 'GET')).toBe(true);
});

it('does not reuse the previous account instance recipient after the same source is reopened', async () => {
  let accountReads = 0;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    if (new URL(request.url).pathname.endsWith('/eligibility')) return json({ allowed: true });
    accountReads++; return json([{ accountId: 'recipient', code: 'receiver', name: '接收人', departmentId: null }]);
  }));
  deactivate = instance.activate();
  let state: { dirty: boolean; ready: boolean } | undefined;
  let rotate: () => void = () => {};
  function Harness() {
    const [value, setValue] = useState<unknown>(); const [session, setSession] = useState(instance);
    rotate = () => { deactivate?.(); const next = new ActionInstanceScope(); deactivate = next.activate(); setSession(next); };
    return <ObjectOwnershipTransferInput action={{ key: 'transfer', labelKey: '转交' }} instance={session} featurePosition="transfer"
      target={{ savePath: '/transfer', eligibilityPath: '/transfer/eligibility', accountsPath: '/transfer/accounts' }}
      value={value} onChange={setValue} onDraftStateChange={next => { state = next; }} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><Harness /></LocalizationProvider>));
  await vi.waitFor(() => expect(accountReads).toBe(1));
  await act(async () => document.querySelector('[role="combobox"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.querySelector('[title="接收人 (receiver)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="接收人 (receiver)"]')!.click());
  await vi.waitFor(() => expect(state?.ready).toBe(true));
  await act(async () => rotate());
  await vi.waitFor(() => expect(accountReads).toBe(2));
  await act(async () => {});
  expect(state?.ready).toBe(false);
  expect(document.querySelector('.ant-select-selection-item')).toBeNull();
});

it('keeps a definite permission denial separate from an unknown outcome and blocks the stale input', async () => {
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    return new URL(request.url).pathname.endsWith('/eligibility') ? json({ allowed: true })
      : json([{ accountId: 'recipient', code: 'receiver', name: '接收人', departmentId: null }]);
  }));
  deactivate = instance.activate();
  const target = { savePath: '/transfer', eligibilityPath: '/transfer/eligibility', accountsPath: '/transfer/accounts' };
  const draft = { recipientId: 'recipient', source: ownershipTransferSource(target, 'transfer', instance) };
  let state: { dirty: boolean; ready: boolean } | undefined;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  const render = (failure?: unknown) => <LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    <ObjectOwnershipTransferInput action={{ key: 'transfer', labelKey: '转交' }} instance={instance} featurePosition="transfer" target={target}
      value={draft} onChange={() => {}} submissionFailure={failure} onDraftStateChange={next => { state = next; }} /></LocalizationProvider>;
  await act(async () => root!.render(render()));
  await vi.waitFor(() => expect(state?.ready).toBe(true));
  await act(async () => root!.render(render(new StandardCrudRequestError('permission was revoked', 403))));
  await vi.waitFor(() => expect(state?.ready).toBe(false));
  expect(document.querySelector('.ant-select-disabled')).not.toBeNull();
  expect(document.body.textContent).not.toContain('转交结果未知');
});

it.each(['target', 'declaration'] as const)('aborts the old read and discards late candidates when the %s changes', async boundary => {
  const selected = ["Part", "Document"].map(moduleId => (declarations as ActionDataAccessRequirement[]).filter(item => item.moduleId === moduleId && item.businessBinding?.endsWith(".transfer") && item.surface === "rowActions" && item.actionKey === "transfer"));
  selected.forEach(items => expect(items).toHaveLength(1));
  const [original, changed] = selected.map(items => items[0]);
  let release: (response: Response) => void = () => {};
  let oldRequest: Request | undefined;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('/eligibility')) return json({ allowed: true });
    if (path.endsWith('/accounts') && (boundary === 'target' ? path === '/old/accounts'
      : request.headers.get('X-NewP-Button') === original.id)) {
      oldRequest = request; return new Promise<Response>(resolve => { release = resolve; });
    }
    if (path.endsWith('/accounts')) return json([{ accountId: 'current', code: 'current', name: '当前候选', departmentId: null }]);
    throw new Error('Unexpected transfer query ' + request.url);
  }));
  deactivate = instance.activate();
  let changeSource: () => void = () => {};
  function Harness() {
    const [source, setSource] = useState('old');
    changeSource = () => setSource('current');
    const path = boundary === 'target' ? '/' + source : '/same-target';
    return <ObjectOwnershipTransferInput action={{ key: 'transfer', labelKey: '转交', dataAccess: source === 'old' ? original : changed }} instance={instance} featurePosition="transfer"
      target={{ savePath: path, eligibilityPath: path + '/eligibility', accountsPath: path + '/accounts' }}
      value={undefined} onChange={() => {}} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><Harness /></LocalizationProvider>));
  await vi.waitFor(() => expect(oldRequest).toBeDefined());
  await act(async () => changeSource());
  expect(oldRequest!.signal.aborted).toBe(true);
  await act(async () => release(json([{ accountId: 'old', code: 'old', name: '旧来源秘密候选', departmentId: null }])));
  await act(async () => document.querySelector('[role="combobox"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.querySelector('[title="当前候选 (current)"]')).not.toBeNull());
  expect(document.body.textContent).not.toContain('旧来源秘密候选');
});
