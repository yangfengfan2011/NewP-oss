// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { Outlet } from 'react-router-dom';
import App from '../App';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const runtimeLocalizationManifest = {
  defaultLanguage: 'zhs',
  supportedLanguages: [
    { code: 'zhs', displayName: '简体中文' },
    { code: 'en', displayName: 'English' },
  ],
  catalogs: {
    zhs: {
      'Common.SystemTitle': 'InforCenter',
      'Common.OK': '确定',
      'Common.Cancel': '取消',
      'Platform.Prompt': '提示',
    },
    en: {
      'Common.SystemTitle': 'InforCenter',
      'Common.OK': 'OK',
      'Common.Cancel': 'Cancel',
      'Platform.Prompt': 'Prompt',
    },
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

  it('loads runtime localization manifest before rendering the localization sample', async () => {
    let resolveResponse!: (response: Response) => void;
    const fetchMock = vi.fn().mockReturnValue(
      new Promise<Response>((resolve) => {
        resolveResponse = resolve;
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    container = document.createElement('div');
    document.body.appendChild(container);
    window.history.pushState({}, '', '/');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
    });

    expect(fetchMock).toHaveBeenCalledWith('/plugins/localization-manifest.json', {
      credentials: 'same-origin',
    });
    expect(container.textContent).not.toContain('语言资源样例');

    await act(async () => {
      resolveResponse(
        new Response(JSON.stringify(runtimeLocalizationManifest), {
          status: 200,
          headers: {
            'Content-Type': 'application/json',
          },
        }),
      );
      await Promise.resolve();
    });

    expect(container.textContent).toContain('语言资源样例');
    expect(container.textContent).toContain('Common.SystemTitle: InforCenter');
    expect(container.textContent).toContain('Common.OK: 确定');
    expect(container.textContent).toContain('Common.Cancel: 取消');
    expect(container.textContent).toContain('Platform.Prompt: 提示');
  });
});
