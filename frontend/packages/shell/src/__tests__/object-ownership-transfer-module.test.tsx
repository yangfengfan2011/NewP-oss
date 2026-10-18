// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { LocalizationProvider, invalidatePlatformAntiforgeryToken } from '@icp-platform/core';
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import { loadComponentSource } from '../../test-support/componentTestInputs';
import metadata from '../../../../modules/infor-center/workspace/metadata/views/WorkspaceFolderList.view.json';
import positions from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceTransferFeatures.contract.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
import workspaceDictionary from '../../../../modules/infor-center/workspace/metadata/dictionaries/Workspace.dic.json';
import { AuthProvider } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
await import('../../../ui/src/ObjectOwnershipTransferInput');
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
const binding = workspace.standardCrudCapabilities!.find(item => item.targetKey === metadata.view.key)!;
const { default: registration } = await binding.load();
it.each(['directory', 'keyboard search', 'revoked search'] as const)('uses the real Workspace row transfer declaration and instance: %s', async scenario => {
  const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['container'] };
  const user = { id: 'owner', ecode: 'owner', ename: '原所属人', revision: 1, mustChangePassword: false,
    featurePositions: [positions.readPosition, positions.transferPosition] };
  const requests: Request[] = [];
  const pendingSearches = new Map<string, (response: Response) => void>();
  let denyEligibility = false;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init); requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.endsWith('/transfer/eligibility')) return denyEligibility ? new Response(null, { status: 403 }) : json({ allowed: true });
    if (url.pathname.endsWith('/transfer/accounts')) {
      const query = url.searchParams.get('query') ?? '';
      if (scenario !== 'directory' && query) return new Promise<Response>(resolve => pendingSearches.set(query, resolve));
      return json(scenario === 'directory' ? [{ accountId: 'recipient', code: 'receiver', name: '接收人', departmentId: null }] : []);
    }
    if (url.pathname === '/api/workspace/ownership/transfer' && request.method === 'POST') return json({ succeeded: true, changed: 1 });
    throw new Error(`Unexpected module transfer request ${request.method} ${url.pathname}`);
  }));
  const declaration = metadata.view.rowActions.find(action => action.key === 'transfer')!;
  const instance = new ActionInstanceScope(); const deactivate = instance.activate();
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const catalog = Object.fromEntries([
    ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]),
    ...Object.entries(workspaceDictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs]),
  ]);
  onTestFinished(async () => { await act(async () => { root.unmount(); for (const finish of pendingSearches.values()) finish(json([])); }); deactivate(); container.remove(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); });
  await act(async () => root.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" initialLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}><AuthProvider initialUser={user}><CurrentPermissionScope>
      <StandardCrudActions actions={[{ ...declaration, labelKey: workspaceDictionary.Workspace.Transfer.zhs }]} handlers={registration.actionHandlers}
        actionInputs={registration.actionInputRenderers} actionEligibilityResolvers={registration.actionEligibilityResolvers}
        featureResolutionForAction={() => ({ kind: 'allowed', position: positions.transferPosition })} refresh={vi.fn()}
        context={{ id: 'original-reference', row: { LID: 'original-reference', ObjectModelKey: 'Part', ObjectId: 'original-object', Name: '零组件' },
          queryScope: { ...position, linkIds: JSON.stringify(position.linkIds) }, instance }} />
    </CurrentPermissionScope></AuthProvider></LocalizationProvider></MemoryRouter>));
  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === label);
  await vi.waitFor(() => expect(button('转交')).toBeDefined());
  await act(async () => button('转交')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="combobox"]')).not.toBeNull());
  const account = document.querySelector<HTMLInputElement>('[role="combobox"]')!;
  await vi.waitFor(() => expect(account.disabled).toBe(false));
  await act(async () => account.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  if (scenario !== 'directory') {
    denyEligibility = scenario === 'revoked search';
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    for (const value of scenario === 'revoked search' ? ['r'] : ['r', 're', 'rec', 'receiver']) {
      await act(async () => {
        account.focus();
        setValue.call(account, value);
        account.dispatchEvent(new Event('input', { bubbles: true }));
      });
      if (scenario === 'revoked search') {
        await vi.waitFor(() => expect(account.disabled).toBe(true));
        expect(button('确认')?.disabled).toBe(true);
        expect(requests.some(request => request.method === 'POST')).toBe(false);
        return;
      }
      expect(account.disabled).toBe(false);
      expect(account.value).toBe(value);
      expect(button('确认')?.disabled).toBe(true);
      await vi.waitFor(() => expect(pendingSearches.has(value)).toBe(true));
    }
    await act(async () => pendingSearches.get('receiver')!(json([{ accountId: 'recipient', code: 'receiver', name: '接收人', departmentId: null }])));
  }
  await vi.waitFor(() => expect(document.querySelector('[title="接收人 (receiver)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="接收人 (receiver)"]')!.click());
  await vi.waitFor(() => expect(button('确认')?.disabled).toBe(false));
  await act(async () => button('确认')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  const writes = requests.filter(request => request.method === 'POST' && new URL(request.url).pathname.endsWith('/transfer'));
  expect(writes).toHaveLength(1);
  expect(await writes[0].clone().json()).toEqual({ position, linkIds: ['original-reference'], recipientId: 'recipient' });
  expect(writes[0].headers.get('X-NewP-Feature')).toBe(positions.transferPosition);
});
