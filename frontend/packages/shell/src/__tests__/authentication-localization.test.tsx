import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LanguageSelect, LocalizationProvider, useLocalization } from '@icp-platform/core';
import { platformFetch, invalidatePlatformAntiforgeryToken } from '../../../core/src/http/platformFetch';
import { AuthenticationOnlyApp } from '../AuthenticationOnlyApp';
import { AuthenticationFrame, LocalizedBusinessBoundary, RuntimeLocalizationProvider } from '../localization/RuntimeLocalizationProvider';
import bootstrap from '../localization/bootstrapLocalization.generated.json';

let root: Root;
let container: HTMLDivElement;
beforeEach(() => {
  invalidatePlatformAntiforgeryToken();
  localStorage.clear();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  window.history.replaceState({}, '', '/login');
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});
async function render(node: ReactNode) { await act(async () => root.render(node)); }
async function selectLanguage(code: string) {
  await act(async () => {
    const select = container.querySelector('select')!;
    select.value = code;
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
}
async function input(id: string, value: string) {
  await act(async () => {
    const field = container.querySelector<HTMLInputElement>(`#${id}`)!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(field, value);
    field.dispatchEvent(new Event('input', { bubbles: true }));
  });
}
const pathOf = (input: RequestInfo | URL) => input instanceof Request ? new URL(input.url).pathname : String(input);
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });

describe('browser language preference', () => {
  function Probe() { const { currentLanguage } = useLocalization(); return <><LanguageSelect /><output>{currentLanguage}</output></>; }
  const provider = (defaultLanguage = 'zhs') => <LocalizationProvider {...bootstrap} defaultLanguage={defaultLanguage} storageKey="icp.language"><Probe /></LocalizationProvider>;
  it('uses the platform default, ignores browser locale and restores a saved choice on remount', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US');
    await render(provider());
    expect(container.querySelector('output')?.textContent).toBe('zhs');
    await selectLanguage('en');
    await render(null);
    await render(provider());
    expect(container.querySelector('select')?.value).toBe('en');
  });
  it('rejects stale preferences and follows the authoritative default when there is no valid choice', async () => {
    localStorage.setItem('icp.language', 'removed');
    await render(provider());
    expect(container.querySelector('select')?.value).toBe('zhs');
    await render(provider('en'));
    expect(container.querySelector('select')?.value).toBe('en');
  });
  it.each(['getItem', 'setItem'] as const)('keeps page-local switching available when storage %s throws', async method => {
    vi.spyOn(Storage.prototype, method).mockImplementation(() => { throw new DOMException('denied', 'SecurityError'); });
    await render(provider());
    await selectLanguage('en');
    expect(container.querySelector('select')?.value).toBe('en');
    expect(container.querySelector('select')?.getAttribute('aria-label')).toBe('Language');
  });
});

describe('authentication and dictionary recovery', () => {
  it('localizes required login feedback without sending an empty form', async () => {
    const fetcher = vi.fn().mockResolvedValue(json(bootstrap));
    vi.stubGlobal('fetch', fetcher);
    await render(<AuthenticationOnlyApp />);
    await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    await selectLanguage('en');
    expect(container.querySelector('.sh-form-error')?.textContent).toBe('Enter Account (ECODE).');
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
  it('keeps an uncertain sign-in result explicit after language switching without another write', async () => {
    const fetcher = vi.fn(async (request: RequestInfo | URL) => {
      const path = pathOf(request);
      if (path.includes('localization')) return json(bootstrap);
      if (path.includes('antiforgery')) return json({ requestToken: 'token' });
      throw new TypeError('connection lost');
    });
    vi.stubGlobal('fetch', fetcher);
    vi.spyOn(console, 'error').mockImplementation(() => {});
    await render(<AuthenticationOnlyApp />);
    await input('ecode', 'account');
    await input('password', 'password');
    await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    expect(container.querySelector('.sh-form-error')?.textContent).toContain('操作结果尚未确认');
    await selectLanguage('en');
    expect(container.querySelector('.sh-form-error')?.textContent).toBe('The operation outcome is unknown. Check the result before deciding whether to retry.');
    expect(fetcher.mock.calls.filter(([request]) => pathOf(request) === '/api/auth/login')).toHaveLength(1);
  });
  it('keeps login drafts and language through repeated failures and successful recovery', async () => {
    const fetcher = vi.fn().mockRejectedValueOnce(new TypeError('offline')).mockRejectedValueOnce(new TypeError('still offline')).mockResolvedValue(json(bootstrap));
    vi.stubGlobal('fetch', fetcher);
    await render(<AuthenticationOnlyApp />);
    await input('ecode', 'draft-account');
    await input('password', 'draft-password');
    const account = container.querySelector('#ecode');
    await selectLanguage('en');
    for (let retry = 0; retry < 2; retry++) {
      await act(async () => Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Retry')!.click());
      expect(container.querySelector('#ecode')).toBe(account);
      expect(container.querySelector<HTMLInputElement>('#password')?.value).toBe('draft-password');
      expect(container.querySelector('select')?.value).toBe('en');
    }
    expect(container.querySelector('.sh-localization-recovery')).toBeNull();
    expect(fetcher).toHaveBeenCalledTimes(3);
  });
  it('withholds business content until a valid dictionary arrives and sends the selected locale', async () => {
    let resolve!: (response: Response) => void;
    const fetcher = vi.fn().mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; })).mockResolvedValue(json({}));
    vi.stubGlobal('fetch', fetcher);
    await render(<RuntimeLocalizationProvider><LocalizedBusinessBoundary><div>business-ready</div></LocalizedBusinessBoundary></RuntimeLocalizationProvider>);
    expect(container.textContent).not.toContain('business-ready');
    await selectLanguage('en');
    await act(async () => resolve(json(bootstrap)));
    expect(container.textContent).toContain('business-ready');
    await platformFetch('/api/read');
    expect((fetcher.mock.calls[1][0] as Request).headers.get('X-ICP-Locale')).toBe('en-US');
  });
  it('ignores a late load after its provider unmounts', async () => {
    let resolve!: (response: Response) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(done => { resolve = done; })));
    await render(<RuntimeLocalizationProvider><AuthenticationFrame><input defaultValue="draft" /></AuthenticationFrame></RuntimeLocalizationProvider>);
    await selectLanguage('en');
    await render(<div>new owner</div>);
    await act(async () => resolve(json(bootstrap)));
    expect(container.textContent).toBe('new owner');
    expect(localStorage.getItem('icp.language')).toBe('en');
  });
  it('retranslates a failed sign-in without another write or losing input', async () => {
    const fetcher = vi.fn(async (request: RequestInfo | URL) => pathOf(request).includes('localization') ? json(bootstrap)
      : pathOf(request).includes('antiforgery') ? json({ requestToken: 'token' })
        : json({ code: 'AUTH_ADMISSION_DENIED', detail: 'raw diagnostic' }, 401));
    vi.stubGlobal('fetch', fetcher);
    await render(<AuthenticationOnlyApp />);
    await input('ecode', 'account');
    await input('password', 'wrong');
    await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    expect(container.querySelector('.sh-form-error')?.textContent).toContain('账号当前不满足登录条件');
    await selectLanguage('en');
    expect(container.querySelector('.sh-form-error')?.textContent).toContain('not eligible to sign in');
    expect(fetcher.mock.calls.filter(([url]) => pathOf(url) === '/api/auth/login')).toHaveLength(1);
    expect(container.querySelector<HTMLInputElement>('#ecode')?.value).toBe('account');
  });
  it('retranslates first-password validation, preserves all drafts, and retains the choice after sign-out', async () => {
    window.history.replaceState({}, '', '/account/change-password');
    vi.stubGlobal('fetch', vi.fn(async (request: RequestInfo | URL) => pathOf(request).includes('localization') ? json(bootstrap)
      : pathOf(request).includes('antiforgery') ? json({ requestToken: 'token' }) : new Response(null, { status: 204 })));
    await render(<AuthenticationOnlyApp initialUser={{ id: 'user', ecode: 'user', ename: 'User', revision: 1, mustChangePassword: true, isLocalAdministrator: false }} />);
    await input('current-password', 'temporary');
    await input('new-password', 'new-password');
    await input('confirm-password', 'different');
    await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    await selectLanguage('en');
    expect(container.querySelector('.sh-form-error')?.textContent).toBe('The new passwords do not match.');
    expect(container.querySelector<HTMLInputElement>('#new-password')?.value).toBe('new-password');
    await act(async () => Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Sign out')!.click());
    expect(container.querySelector('h2')?.textContent).toBe('Sign in to the platform');
    expect(container.querySelector('select')?.value).toBe('en');
  });
  it('retranslates the real password-change rejection and keeps the draft without resubmitting', async () => {
    window.history.replaceState({}, '', '/account/change-password');
    const fetcher = vi.fn(async (request: RequestInfo | URL) => pathOf(request).includes('localization') ? json(bootstrap)
      : pathOf(request).includes('antiforgery') ? json({ requestToken: 'token' })
        : json({ code: 'CREDENTIAL_CURRENT_INVALID', errors: { CurrentPassword: ['CREDENTIAL_CURRENT_INVALID'] } }, 400));
    vi.stubGlobal('fetch', fetcher);
    await render(<AuthenticationOnlyApp initialUser={{ id: 'user', ecode: 'user', ename: 'User', revision: 1, mustChangePassword: true, isLocalAdministrator: false }} />);
    await input('current-password', 'incorrect');
    await input('new-password', 'new-password');
    await input('confirm-password', 'new-password');
    await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    expect(container.querySelector('.sh-form-error')?.textContent).toBe('当前密码不正确。');
    await selectLanguage('en');
    expect(container.querySelector('.sh-form-error')?.textContent).toBe('The current password is incorrect.');
    expect(container.querySelector<HTMLInputElement>('#new-password')?.value).toBe('new-password');
    expect(fetcher.mock.calls.filter(([request]) => pathOf(request) === '/api/identity/me/password-change')).toHaveLength(1);
  });
});
