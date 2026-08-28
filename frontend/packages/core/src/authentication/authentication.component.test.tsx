// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  FirstPasswordChangePage,
  RestrictedSessionGuard,
  useAuthenticationState,
  type AuthenticationBootstrapSnapshot,
  type AuthenticationState,
} from './index';
import { invalidatePlatformAntiforgeryToken } from '../http';

const authenticatedUser = {
  id: 'User_1',
  ecode: 'alice',
  ename: 'Alice',
  permissions: [],
  mustChangePassword: false,
  revision: 1,
  isLocalAdministrator: false,
  timeZoneId: 'Asia/Taipei',
};

let observedAuthentication: AuthenticationState | undefined;

function AuthenticationProbe({ initialSnapshot }: { initialSnapshot?: AuthenticationBootstrapSnapshot }) {
  observedAuthentication = useAuthenticationState(initialSnapshot);
  return null;
}

describe('authentication components', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
    observedAuthentication = undefined;
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('keeps a known subject and exposes a retryable stable error during current-user outage', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedUser))
      .mockResolvedValueOnce(problemResponse(503, 'AUTH_SESSION_STORE_UNAVAILABLE'))
      .mockRejectedValueOnce(new TypeError('network down'))
      .mockResolvedValueOnce(jsonResponse(authenticatedUser))
      .mockResolvedValueOnce(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    await renderAuthenticationProbe();

    let outage: unknown;
    await act(async () => {
      try {
        await observedAuthentication!.retrySessionValidation();
      } catch (error) {
        outage = error;
      }
    });

    expect(outage).toMatchObject({ message: 'AUTH_SESSION_STORE_UNAVAILABLE' });
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
    expect(observedAuthentication?.sessionError).toBe('AUTH_SESSION_STORE_UNAVAILABLE');

    await act(async () => {
      await expect(observedAuthentication!.retrySessionValidation()).rejects.toMatchObject({
        message: 'AUTH_NETWORK_UNAVAILABLE',
      });
    });
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
    expect(observedAuthentication?.sessionError).toBe('AUTH_NETWORK_UNAVAILABLE');

    await act(async () => observedAuthentication!.retrySessionValidation());
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
    expect(observedAuthentication?.sessionError).toBeUndefined();

    await act(async () => observedAuthentication!.retrySessionValidation());
    expect(observedAuthentication?.user).toBeUndefined();
    expect(observedAuthentication?.sessionError).toBeUndefined();
  });

  it('does not clear a known subject when logout receives 503 or a network failure, but clears it on 401', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(authenticatedUser))
      .mockResolvedValueOnce(jsonResponse({ requestToken: 'csrf-token' }))
      .mockResolvedValueOnce(problemResponse(503, 'AUTH_SESSION_STORE_UNAVAILABLE'))
      .mockRejectedValueOnce(new TypeError('network down'))
      .mockResolvedValueOnce(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    await renderAuthenticationProbe();

    await act(async () => {
      await expect(observedAuthentication!.logout()).rejects.toMatchObject({
        message: 'AUTH_SESSION_STORE_UNAVAILABLE',
      });
    });
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
    expect(observedAuthentication?.sessionError).toBe('AUTH_SESSION_STORE_UNAVAILABLE');

    await act(async () => {
      await expect(observedAuthentication!.logout()).rejects.toMatchObject({
        message: 'AUTH_NETWORK_UNAVAILABLE',
      });
    });
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
    expect(observedAuthentication?.sessionError).toBe('AUTH_NETWORK_UNAVAILABLE');

    await act(async () => observedAuthentication!.logout());
    expect(observedAuthentication?.user).toBeUndefined();
    expect(observedAuthentication?.sessionError).toBeUndefined();
  });

  it('uses a verified bootstrap snapshot without repeating current-user in StrictMode', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <StrictMode>
          <AuthenticationProbe initialSnapshot={{ user: authenticatedUser }} />
        </StrictMode>,
      );
    });

    expect(observedAuthentication?.isInitialized).toBe(true);
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('uses the login response as the current subject without a follow-up read', async () => {
    invalidatePlatformAntiforgeryToken();
    vi.spyOn(Intl, 'DateTimeFormat').mockImplementation(() => ({
      resolvedOptions: () => ({ timeZone: 'Asia/Taipei' }),
    }) as Intl.DateTimeFormat);
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/auth/antiforgery-token')) {
        return jsonResponse({ requestToken: 'csrf-token' });
      }
      if (url.endsWith('/api/auth/login')) {
        return jsonResponse(authenticatedUser);
      }
      throw new Error(`Unexpected request: ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<AuthenticationProbe initialSnapshot={{ user: undefined }} />);
    });

    await act(async () => observedAuthentication!.login('admin', 'password'));

    const requestedUrls = fetchMock.mock.calls.map(([input]) => (
      input instanceof Request ? input.url : String(input)
    ));
    expect(requestedUrls.some((url) => url.endsWith('/api/auth/current-user'))).toBe(false);
    const loginRequest = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input) => input instanceof Request && input.url.endsWith('/api/auth/login'));
    expect(JSON.parse(await (loginRequest as Request).clone().text())).toMatchObject({
      ecode: 'admin',
      password: 'password',
      timeZoneId: 'Asia/Taipei',
    });
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
  });

  it('renders only the restricted branch for a temporary session', async () => {
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<RestrictedSessionGuard mustChangePassword restricted={<span>change</span>} regular={<span>business</span>} />);
    });
    expect(container.textContent).toBe('change');
  });

  it('presents the first-password-change form without business navigation', async () => {
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    const auth: AuthenticationState = {
      isAuthenticated: true,
      isInitialized: true,
      sessionError: undefined,
      user: {
        id: 'User_1', ecode: 'alice', ename: 'Alice', permissions: [], mustChangePassword: true,
        revision: 1, isLocalAdministrator: false,
        timeZoneId: 'Asia/Taipei',
      },
      login: vi.fn(),
      logout: vi.fn(),
      retrySessionValidation: vi.fn(),
      changePassword: vi.fn(),
    };
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<FirstPasswordChangePage auth={auth} />);
    });
    expect(container.textContent).toContain('设置正式密码');
    expect(container.querySelector('#current-password')).not.toBeNull();
    expect(container.querySelector('#new-password')).not.toBeNull();
  });

  async function renderAuthenticationProbe() {
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<AuthenticationProbe />);
    });
    expect(observedAuthentication?.isInitialized).toBe(true);
    expect(observedAuthentication?.user).toEqual(authenticatedUser);
  }
});

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

function problemResponse(status: number, code: string): Response {
  return new Response(JSON.stringify({ code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}
