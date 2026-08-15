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
  });

  afterEach(async () => {
    if (root) {
      await act(async () => root?.unmount());
    }
    container.remove();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
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
