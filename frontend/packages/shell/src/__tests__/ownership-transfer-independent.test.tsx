// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { LocalizationProvider, invalidatePlatformAntiforgeryToken } from '@icp-platform/core';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
import { AuthProvider } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
await import('../../../ui/src/ObjectOwnershipTransferInput');
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
it.each(['Part', 'Document'])('uses the actual %s transfer action and compiled owner position with the canonical EID', async owner => {
  const module = (await loadComponentSource<{ default: FrontendBehaviorBindings }>(owner, 'index.ts')).default;
  const metadata = readComponentJson(owner, `metadata/views/${owner}List.view.json`);
  const dictionary = readComponentJson(owner, `metadata/dictionaries/${owner}.dic.json`)[owner];
  const positions = JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${owner}TransferFeatures.contract.json`), 'utf8'));
  const base = `/api/${owner.toLowerCase()}/${owner === 'Part' ? 'parts' : 'documents'}/original-object/transfer`;
  const user = { id: 'owner', ecode: 'owner', ename: '原所属人', revision: 1, mustChangePassword: false,
    featurePositions: [positions.readPosition, positions.transferPosition] };
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init); requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.endsWith('/transfer/eligibility')) return json({ allowed: true });
    if (url.pathname.endsWith('/transfer/accounts')) return json([{ accountId: 'recipient', code: 'receiver', name: '接收人', departmentId: null }]);
    if (url.pathname === base && request.method === 'POST') return json({ succeeded: true, changed: 1 });
    throw new Error(`Unexpected module transfer request ${request.method} ${url.pathname}`);
  }));
  const binding = module.standardCrudCapabilities!.find(item => item.targetKey === metadata.view.key)!;
  const { default: registration } = await binding.load();
  const declaration = metadata.view.rowActions.find(action => action.key === 'transfer')!;
  const instance = new ActionInstanceScope(); const deactivate = instance.activate();
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const catalog = Object.fromEntries([
    ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]),
    ...Object.entries(dictionary).map(([key, value]) => [`${owner}.${key}`, (value as { zhs: string }).zhs]),
  ]);
  onTestFinished(async () => { await act(async () => root.unmount()); deactivate(); container.remove(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); });
  await act(async () => root.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}><AuthProvider initialUser={user}><CurrentPermissionScope>
      <StandardCrudActions actions={[{ ...declaration, labelKey: dictionary.Transfer.zhs }]} handlers={registration.actionHandlers}
        actionInputs={registration.actionInputRenderers} actionEligibilityResolvers={registration.actionEligibilityResolvers}
        featureResolutionForAction={() => ({ kind: 'allowed', position: positions.transferPosition })} refresh={vi.fn()}
        context={{ id: 'original-object', row: { EID: 'original-object', ENAME: '原对象' }, instance }} />
    </CurrentPermissionScope></AuthProvider></LocalizationProvider></MemoryRouter>));
  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === label);
  await vi.waitFor(() => expect(button('转交')).toBeDefined());
  await act(async () => button('转交')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="combobox"]')).not.toBeNull());
  await act(async () => document.querySelector('[role="combobox"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.querySelector('[title="接收人 (receiver)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="接收人 (receiver)"]')!.click());
  await vi.waitFor(() => expect(button('确认')?.disabled).toBe(false));
  await act(async () => button('确认')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  const writes = requests.filter(request => request.method === 'POST' && new URL(request.url).pathname.endsWith('/transfer'));
  expect(writes).toHaveLength(1);
  expect(new URL(writes[0].url).pathname).toBe(base);
  expect(await writes[0].clone().json()).toEqual({ recipientId: 'recipient' });
  expect(requests.filter(request => request.method === 'GET' && new URL(request.url).pathname.endsWith('/accounts')).every(request => new URL(request.url).pathname === base + '/accounts')).toBe(true);
  expect(writes[0].headers.get('X-NewP-Feature')).toBe(positions.transferPosition);
});
