// @vitest-environment jsdom

import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { Outlet, useParams } from 'react-router-dom';
import { ModuleRegistry, type AuthUser } from '@icp-platform/sdk';
import App from '../App';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const state = vi.hoisted(() => ({
  user: undefined as AuthUser | undefined,
  isAuthenticated: false,
  localAdministratorOnly: false,
}));

vi.mock('@icp-platform/core', () => ({
  AuthorizationProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  FirstPasswordChangePage: () => <div>change password</div>,
  LocalizationProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  resolveAuthenticationDestination: (mustChangePassword: boolean, destination: string) => (
    mustChangePassword ? '/account/change-password' : destination
  ),
}));

vi.mock('../contexts/AuthContext', () => ({
  AuthProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useAuth: () => ({
    isInitialized: true,
    isAuthenticated: state.isAuthenticated,
    user: state.user,
    sessionError: null,
    retrySessionValidation: vi.fn(),
    hasPermission: (permission: string) => Boolean(state.user?.permissions.includes(permission)),
    hasAnyPermission: (permissions: string[]) => permissions.some(
      (permission) => Boolean(state.user?.permissions.includes(permission)),
    ),
    hasAllPermissions: (permissions: string[]) => permissions.every(
      (permission) => Boolean(state.user?.permissions.includes(permission)),
    ),
  }),
}));

vi.mock('../contexts/PlatformContext', () => ({
  PlatformProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock('../components/Layout', () => ({
  default: () => <Outlet />,
}));

vi.mock('../pages/Home', () => ({ default: () => <div>home</div> }));
vi.mock('../pages/Login', () => ({ default: () => <div>login</div> }));
vi.mock('../pages/UiShowcase', () => ({ default: () => <div>showcase</div> }));
vi.mock('../pages/StandardCrudDetailPageHost', () => ({
  default: ({ viewKey, id }: { viewKey: string; id: string | null }) => (
    <div>{`detail ${viewKey} ${id ?? 'missing'} ${window.location.search}`}</div>
  ),
}));

vi.mock('../localization/runtimeLocalizationManifest', () => ({
  getFallbackRuntimeLocalizationManifest: vi.fn(),
  loadRuntimeLocalizationManifest: vi.fn().mockResolvedValue({
    defaultLanguage: 'zhs',
    supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
    catalogs: { zhs: {} },
  }),
}));

vi.mock('../metadata/runtimeMetadataLoader', () => ({
  getFallbackRuntimeMetadata: vi.fn(),
  loadRuntimeMetadata: vi.fn().mockImplementation(async () => ({
    manifest: { modules: [] },
    navDocuments: [{
      type: 'nav',
      navigation: {
        items: [{
          key: 'example.page',
          path: '/example',
          entryType: 'custom',
          targetKey: 'example.page',
          permissions: ['Example.View'],
          ...(state.localAdministratorOnly ? { localAdministratorOnly: true } : {}),
        }],
      },
    }],
    viewDocuments: [],
    modelDocuments: [],
  })),
}));

describe('route capability authorization ordering', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    state.isAuthenticated = true;
    state.localAdministratorOnly = false;
    state.user = {
      id: 'user-1',
      ecode: 'user',
      ename: 'User',
      permissions: ['Example.View'],
      mustChangePassword: false,
      revision: 1,
      isLocalAdministrator: true,
    };
    window.history.replaceState({}, '', '/example?mode=direct');
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }
    container.remove();
  });

  it('loads the matched capability after access succeeds and preserves the query', async () => {
    function ExamplePage() {
      return <div>{`example capability ${window.location.search}`}</div>;
    }
    const load = vi.fn().mockResolvedValue({ default: ExamplePage });
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'example', displayName: 'Example', version: '1.0.0' },
      routeCapabilities: [{ kind: 'custom', targetKey: 'example.page', load }],
    }], 'snapshot-authorized');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(container.textContent).toContain('example capability ?mode=direct');
    expect(load).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['anonymous', () => {
      state.isAuthenticated = false;
      state.user = undefined;
    }],
    ['without permission', () => {
      state.user = { ...state.user!, permissions: [] };
    }],
    ['not a local administrator', () => {
      state.localAdministratorOnly = true;
      state.user = { ...state.user!, isLocalAdministrator: false };
    }],
  ] as const)('does not invoke the loader for a %s route request', async (_kind, configure) => {
    configure();
    const load = vi.fn().mockResolvedValue({ default: () => <div>should not render</div> });
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'denied', displayName: 'Denied', version: '1.0.0' },
      routeCapabilities: [{ kind: 'custom', targetKey: 'example.page', load }],
    }], `snapshot-denied-${_kind}`);

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(load).not.toHaveBeenCalled();
    expect(container.textContent).not.toContain('should not render');
  });

  it('preserves :id and query semantics for a direct standard CRUD detail route', async () => {
    window.history.replaceState({}, '', '/records/record-42?tab=summary');
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'records', displayName: 'Records', version: '1.0.0' },
      standardCrudResources: [{
        resourceKey: 'records.resource',
        apiRoute: '/api/records',
        listViewKey: 'records.list',
        listRoutePath: '/records',
        detailViewKey: 'records.detail',
        detailRoutePath: '/records/:id',
        permissions: ['Example.View'],
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value) => value as Record<string, unknown>,
        getRowId: () => 'record-42',
      }],
    }], 'snapshot-detail-route');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(container.textContent).toContain('detail records.detail record-42 ?tab=summary');
  });

  it('keeps the legacy new route redirect to the standard CRUD drawer URL', async () => {
    window.history.replaceState({}, '', '/records/new');
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'records-redirect', displayName: 'Records Redirect', version: '1.0.0' },
      standardCrudResources: [{
        resourceKey: 'records-redirect.resource',
        apiRoute: '/api/records',
        listViewKey: 'records-redirect.list',
        listRoutePath: '/records',
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value) => value as Record<string, unknown>,
        getRowId: () => 'record-42',
      }],
    }], 'snapshot-drawer-redirect');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(`${window.location.pathname}${window.location.search}`).toBe('/records?drawer=create');
  });

  it('renders a legacy fallback route through the compatibility Adapter', async () => {
    function LegacyFallbackPage() {
      const params = useParams();
      return <div>{`legacy ${params.id ?? 'missing'} ${window.location.search}`}</div>;
    }
    window.history.replaceState({}, '', '/legacy/item-7?mode=fallback');
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'legacy-fallback', displayName: 'Legacy Fallback', version: '1.0.0' },
      routes: [{
        path: '/legacy/:id',
        component: LegacyFallbackPage,
        permissions: ['Example.View'],
      }],
    }], 'snapshot-legacy-fallback');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(container.textContent).toContain('legacy item-7 ?mode=fallback');
  });
});
