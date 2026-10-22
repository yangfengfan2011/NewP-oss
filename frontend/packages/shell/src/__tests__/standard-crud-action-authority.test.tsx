import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken, LocalizationProvider, platformFetch, useLocalization } from '@icp-platform/core';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { platformTestCatalogs } from './localizationTestCatalogs';

vi.unmock('@icp-platform/core');
const initialUser = { id: 'manager', ecode: 'manager', ename: 'Manager', revision: 1, mustChangePassword: false,
  isLocalAdministrator: false, featurePositions: ['authorize', 'other'], timeZoneId: 'Asia/Taipei' };
let nextUser: typeof initialUser | undefined;
let root: Root;
let container: HTMLDivElement;
let refreshIdentity: () => Promise<void>;
let language: (value: string) => void;
let changePosition: (value: string) => void;
let finish: (failed?: boolean) => void;
let execute: ReturnType<typeof vi.fn>;
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
afterEach(async () => { await act(async () => root?.unmount()); container?.remove(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); });

async function mount() {
  nextUser = initialUser;
  execute = vi.fn(() => new Promise<void>((resolve, reject) => {
    finish = failed => failed ? reject(new Error('write failed')) : resolve();
  }));
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = input instanceof Request ? input.url : String(input);
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'test-token' });
    if (path.endsWith('current-user')) return nextUser ? json(nextUser) : new Response(null, { status: 401 });
    return new Response('{}', { status: 403 });
  }));
  function Consumer() {
    const auth = useAuth(); refreshIdentity = auth.retrySessionValidation;
    language = useLocalization().setLanguage;
    const [position, setPosition] = useState('authorize'); changePosition = setPosition;
    return <StandardCrudActions actions={[{ key: 'authorize', labelKey: 'Authorize', inputKey: 'draft' }]}
      featureResolutionForAction={() => auth.user?.featurePositions?.includes(position)
        ? { kind: 'allowed', position } : { kind: 'denied', position }}
      handlers={{ authorize: execute }} context={{ id: 'Company_1' }} refresh={vi.fn()}
      actionInputs={{ draft: ({ value, onChange }) => <textarea aria-label="Draft" value={String(value ?? '')}
        onChange={event => onChange(event.target.value)} /> }} />;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root.render(<MemoryRouter><LocalizationProvider initialLanguage="zhs" defaultLanguage="zhs" catalogs={platformTestCatalogs}
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]}>
    <AuthProvider initialUser={initialUser}><CurrentPermissionScope><Consumer /></CurrentPermissionScope></AuthProvider>
  </LocalizationProvider></MemoryRouter>));
  await click('Authorize');
  const textarea = document.querySelector('textarea')!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(textarea, 'old authority draft');
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
  });
  return textarea;
}
async function click(label: string) {
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('button')]
    .find(button => button.textContent?.replace(/\s/g, '') === label)!.click());
}

it.each(['subject', 'features', 'epoch', 'position', 'anonymous'])('unmounts old action input when %s changes and prevents its confirmation', async change => {
  await mount();
  await act(async () => {
    if (change === 'position') changePosition('other');
    else if (change === 'epoch') await platformFetch('/denied', { method: 'PUT' });
    else { nextUser = change === 'subject' ? { ...initialUser, id: 'new-manager' }
      : change === 'features' ? { ...initialUser, featurePositions: ['authorize'] } : undefined;
    await refreshIdentity(); }
  });
  expect(document.querySelector('textarea')).toBeNull();
  expect(document.querySelector('.ant-modal')).toBeNull();
  expect(execute).not.toHaveBeenCalled();
  if (change !== 'anonymous') {
    await click('Authorize');
    expect(document.querySelector<HTMLTextAreaElement>('textarea')?.value).toBe('');
  }
});

it.each([false, true])('keeps locale drafts and pending execution, then clears invalidated input after completion (failed: %s)', async failed => {
  const textarea = await mount();
  await act(async () => language('en'));
  expect(document.querySelector('textarea')).toBe(textarea);
  expect(textarea.value).toBe('old authority draft');
  await click('Confirm');
  expect(execute).toHaveBeenCalledTimes(1);
  await act(async () => language('zhs'));
  expect(document.querySelector('textarea')).toBe(textarea);
  expect(execute).toHaveBeenCalledTimes(1);
  nextUser = { ...initialUser, id: 'new-manager' };
  await act(async () => refreshIdentity());
  expect(document.querySelector('textarea')).toBeNull();
  expect(execute).toHaveBeenCalledTimes(1);
  nextUser = initialUser;
  await act(async () => refreshIdentity());
  expect(document.querySelector('textarea')).toBeNull();
  await act(async () => finish(failed));
  expect(document.querySelector('.ant-modal')).toBeNull();
  await click('Authorize');
  expect(document.querySelector<HTMLTextAreaElement>('textarea')?.value).toBe('');
});
