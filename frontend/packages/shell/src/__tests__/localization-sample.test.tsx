// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { Outlet } from 'react-router-dom';
import App from '../App';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const runtimeLocalizationManifest = {
  defaultLanguage: 'zhs',
  supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
  catalogs: {
    zhs: {},
  },
};

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
    getAllModules: () => [],
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

    vi.unstubAllGlobals();
  });

  it('does not render legacy localization sample panels after runtime manifest migration', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(runtimeLocalizationManifest), {
        status: 200,
        headers: {
          'Content-Type': 'application/json',
        },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    container = document.createElement('div');
    document.body.appendChild(container);
    window.history.pushState({}, '', '/');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(fetchMock).toHaveBeenCalledWith('/plugins/localization-manifest.json', {
      credentials: 'same-origin',
    });
    expect(container.textContent).toContain('业务门户');
    expect(container.textContent).toContain('平台门户');
    expect(container.textContent).not.toContain('语言资源样例');
    expect(container.textContent).not.toContain('枚举资源样例');
  });
});
