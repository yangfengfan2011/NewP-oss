// @vitest-environment jsdom

import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRoot, Root } from 'react-dom/client';
import App from '../App';

vi.mock('../contexts/AuthContext', () => ({
  AuthProvider: ({ children }: { children: React.ReactNode }) => children,
  useAuth: () => ({
    isAuthenticated: false,
    isInitialized: true,
    user: undefined,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: () => false,
    hasAnyPermission: () => false,
    hasAllPermissions: () => false,
  }),
}));

describe('login page fallback', () => {
  let container: HTMLDivElement;
  let root: Root;
  let consoleWarnSpy: ReturnType<typeof vi.spyOn>;
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.appendChild(container);
    window.history.replaceState({}, '', '/login');
    vi.stubGlobal(
      'fetch',
      vi.fn((input: string | URL | Request) => {
        const requestUrl = typeof input === 'string'
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;

        if (requestUrl.endsWith('/plugins/localization-manifest.json')) {
          return Promise.resolve(
            new Response('', {
              status: 404,
            }),
          );
        }

        return Promise.resolve(
          new Response('', {
            status: 401,
          }),
        );
      }),
    );
    consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container.remove();
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    consoleWarnSpy.mockRestore();
    consoleErrorSpy.mockRestore();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('keeps the login route renderable when the runtime localization manifest is unavailable', async () => {
    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
    });

    expect(container.textContent).toContain('登录平台');
    expect(container.textContent).toContain('登录账号（ECODE）');
    expect(container.textContent).toContain('密码');
  });
});
