// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { Outlet } from 'react-router-dom';
import App from '../App';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('@icp-platform/core', async () => {
  const actual = await vi.importActual<typeof import('@icp-platform/core')>('@icp-platform/core');

  return {
    ...actual,
    AuthorizationProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  };
});

vi.mock('../contexts/PlatformContext', () => ({
  PlatformProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  usePlatform: () => ({
    shellView: 'business',
    uiConfig: {
      shellExperienceMode: 'hybrid',
    },
  }),
}));

vi.mock('../contexts/AuthContext', () => ({
  AuthProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useAuth: () => ({
    isInitialized: true,
    isAuthenticated: true,
    user: { id: 'user-1' },
  }),
}));

vi.mock('../components/PermissionGuard', () => ({
  PermissionGuard: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock('../components/Layout', () => ({
  default: () => <Outlet />,
}));

vi.mock('../pages/Login', () => ({
  default: () => <div>login</div>,
}));

vi.mock('@icp-platform/sdk', () => ({
  ModuleRegistry: {
    getRoutes: () => [],
  },
}));

describe('Home localization sample', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('shows the enumeration sample block on Home with fallback labels for SelectMode', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    window.history.pushState({}, '', '/');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
    });

    const enumerationPanel = Array
      .from(container.querySelectorAll('.sh-page-panel'))
      .find((panel) => panel.textContent?.includes('枚举资源样例'));

    expect(enumerationPanel).not.toBeUndefined();
    expect(enumerationPanel?.textContent).toContain('枚举资源样例');
    expect(enumerationPanel?.textContent).toContain('Gender');
    expect(enumerationPanel?.textContent).toContain('Female');
    expect(enumerationPanel?.textContent).toContain('女');
    expect(enumerationPanel?.textContent).toContain('MessageState');
    expect(enumerationPanel?.textContent).toContain('Read');
    expect(enumerationPanel?.textContent).toContain('已读');
    expect(enumerationPanel?.textContent).toContain('SelectMode');
    expect(enumerationPanel?.textContent).toContain('MULTI');
    expect(enumerationPanel?.textContent).toContain('SINGLE');
    expect(enumerationPanel?.textContent).toContain('ItemName');
  });
});
