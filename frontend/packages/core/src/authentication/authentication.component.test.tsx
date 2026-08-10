// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FirstPasswordChangePage, RestrictedSessionGuard, type AuthenticationState } from './index';

describe('authentication components', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
    (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = false;
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
      user: {
        id: 'User_1', username: 'alice', displayName: 'Alice', permissions: [], mustChangePassword: true,
        revision: 1, isLocalAdministrator: false,
      },
      login: vi.fn(),
      logout: vi.fn(),
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
});
