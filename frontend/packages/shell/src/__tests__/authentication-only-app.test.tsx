// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import type { AuthUser } from '@icp-platform/sdk';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    Navigate: ({ to }: { to: string }) => <span data-auth-redirect={to} />,
  };
});

import { AuthenticationOnlyApp } from '../AuthenticationOnlyApp';

const regularUser: AuthUser = {
  id: 'User_1',
  ecode: 'admin',
  ename: 'Administrator',
  permissions: [],
  mustChangePassword: false,
  revision: 1,
  isLocalAdministrator: true,
};

describe('AuthenticationOnlyApp authenticated transition', () => {
  let root: Root | undefined;
  let container: HTMLDivElement;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    window.history.replaceState({}, '', '/login');
    container = document.createElement('div');
    document.body.appendChild(container);
    localStorage.clear();
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
  });

  afterEach(async () => {
    if (root) {
      await act(async () => root?.unmount());
    }
    container.remove();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
    vi.unstubAllGlobals();
  });

  it('offers both languages while offline and preserves login input when switching', async () => {
    await act(async () => {
      root = createRoot(container);
      root.render(<AuthenticationOnlyApp />);
    });
    const account = container.querySelector<HTMLInputElement>('#ecode')!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(account, 'draft-account');
      account.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const language = container.querySelector('[role="combobox"][aria-label="语言"]');
    expect(language).not.toBeNull();
    expect(container.querySelector('.sh-authentication-toolbar')?.textContent).not.toContain('语言');
    await act(async () => {
      language!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    });
    expect(Array.from(document.querySelectorAll('.ant-select-item-option')).map(option => option.textContent))
      .toEqual(['简体中文', 'English']);
    await act(async () => {
      document.querySelector<HTMLElement>('.ant-select-item-option[title="English"]')!.click();
    });
    expect(container.querySelector('h2')?.textContent).toBe('Sign in to the platform');
    expect(container.querySelector('#ecode')).toBe(account);
    expect(account.value).toBe('draft-account');
    expect(localStorage.getItem('icp.language')).toBe('en');
    expect(container.textContent).toContain('Retry');
  });

  it('does not redirect a regular authenticated user inside the authentication-only router', async () => {
    await act(async () => {
      root = createRoot(container);
      root.render(<AuthenticationOnlyApp initialUser={regularUser} />);
    });

    expect(container.querySelector('[data-auth-redirect]')).toBeNull();
  });

  it('still redirects a restricted authenticated user to the password-change route', async () => {
    await act(async () => {
      root = createRoot(container);
      root.render(
        <AuthenticationOnlyApp
          initialUser={{ ...regularUser, mustChangePassword: true }}
        />,
      );
    });

    expect(container.querySelector('[data-auth-redirect]')?.getAttribute('data-auth-redirect'))
      .toBe('/account/change-password');
  });
});
