// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import { MemoryRouter, Outlet } from 'react-router-dom';
import App from '../App';
import UiShowcase from '../pages/UiShowcase';

const runtimeLocalizationManifest = {
  defaultLanguage: 'zhs',
  supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
  catalogs: {
    zhs: {},
  },
};

const localizationProps = {
  defaultLanguage: 'zhs',
  supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
  catalogs: {
    zhs: {},
  },
};

vi.mock('../contexts/AuthContext', () => ({
  AuthProvider: ({ children }: { children: React.ReactNode }) => children,
  useAuth: () => ({
    isAuthenticated: true,
    isInitialized: true,
    user: {
      id: 'tester',
      permissions: ['*'],
    },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: () => true,
    hasAnyPermission: () => true,
    hasAllPermissions: () => true,
  }),
}));

vi.mock('../components/Layout', () => ({
  default: () => <Outlet />,
}));

describe('UiShowcase page', () => {
  let container: HTMLDivElement;
  let root: Root;
  let consoleWarnSpy: ReturnType<typeof vi.spyOn>;
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.appendChild(container);
    window.history.replaceState({}, '', '/');
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
            new Response(JSON.stringify(runtimeLocalizationManifest), {
              status: 200,
              headers: {
                'Content-Type': 'application/json',
              },
            }),
          );
        }

        return Promise.resolve(
          new Response(JSON.stringify([]), {
            status: 200,
            headers: {
              'Content-Type': 'application/json',
            },
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

  it('renders the complete showcase groups with real component examples and a modal interaction loop', async () => {
    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider {...localizationProps}>
          <MemoryRouter>
            <UiShowcase />
          </MemoryRouter>
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('控件展示');
    expect(container.textContent).toContain('基础控件');
    expect(container.textContent).toContain('结构型页面块');
    expect(container.textContent).toContain('复合控件');
    expect(container.textContent).toContain('候选控件');

    expect(container.textContent).toContain('Button');
    expect(container.textContent).toContain('Textarea');
    expect(container.textContent).toContain('Select');
    expect(container.textContent).toContain('Tabs');
    expect(container.textContent).toContain('Pagination');

    expect(container.textContent).toContain('TableWrap');
    expect(container.textContent).toContain('StatusBadge');
    expect(container.textContent).toContain('FormInline');

    expect(container.textContent).toContain('Drawer');
    expect(container.textContent).toContain('ConfirmDialog');
    expect(container.textContent).toContain('FilterBar');
    expect(container.textContent).toContain('KeyValueEditor');

    expect(container.textContent).toContain('TreeSelect');
    expect(container.textContent).toContain('DataTable');
    expect(container.textContent).toContain('Uploader');
    expect(container.textContent).toContain('候选能力');

    const openModalButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('打开 Modal 示例'),
    );
    expect(openModalButton).toBeDefined();

    await act(async () => {
      openModalButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(document.body.textContent).toContain('Modal 示例');

    const closeModalButton = Array.from(document.body.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('关闭'),
    );
    expect(closeModalButton).toBeDefined();

    await act(async () => {
      closeModalButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(document.body.textContent).not.toContain('当前输入：');
  });

  it('renders successfully through the /ui-showcase route', async () => {
    window.history.replaceState({}, '', '/ui-showcase');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
    });

    expect(container.textContent).toContain('控件展示');
    expect(container.textContent).toContain('DataTable');
    expect(container.textContent).toContain('Uploader');
  });
});
