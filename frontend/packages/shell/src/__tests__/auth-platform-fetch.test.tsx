// @vitest-environment jsdom

import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import type { AuthContext as AuthContextValue } from '@icp-platform/sdk';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const coreAuthentication = vi.hoisted(() => ({
  isAuthenticated: true,
  isInitialized: true,
  sessionError: undefined,
  user: {
    id: 'User_1',
    ecode: 'admin',
    ename: 'Administrator',
    permissions: ['Identity.Users.Manage'],
    mustChangePassword: false,
    revision: 4,
    isLocalAdministrator: true,
  },
  login: vi.fn(),
  logout: vi.fn(),
  retrySessionValidation: vi.fn(),
  changePassword: vi.fn(),
}));

vi.mock('@icp-platform/core', () => ({
  useAuthenticationState: () => coreAuthentication,
}));

import { AuthProvider, useAuth } from '../contexts/AuthContext';

let observedAuth: AuthContextValue | undefined;

function AuthProbe() {
  observedAuth = useAuth();
  return null;
}

describe('AuthProvider core authentication composition', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  beforeEach(() => {
    observedAuth = undefined;
  });

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
  });

  it('adapts the core state without owning authentication requests', async () => {
    container = document.createElement('div');
    document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<AuthProvider initialUser={coreAuthentication.user}><AuthProbe /></AuthProvider>);
    });

    expect(observedAuth?.user?.revision).toBe(4);
    expect(observedAuth?.user?.isLocalAdministrator).toBe(true);
    expect(observedAuth?.hasPermission('Identity.Users.Manage')).toBe(true);
    expect(observedAuth?.changePassword).toBe(coreAuthentication.changePassword);
  });
});
