// @vitest-environment jsdom

import { act, Component, type ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { Outlet, useParams } from 'react-router-dom';
import { ModuleRegistry, type AuthUser } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/organization/src';
import App from '../App';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

class ProjectionFailureBoundary extends Component<
  { children: ReactNode },
  { message: string | null }
> {
  state = { message: null as string | null };

  static getDerivedStateFromError(error: unknown): { message: string } {
    return { message: error instanceof Error ? error.message : String(error) };
  }

  render(): ReactNode {
    return this.state.message ? <div>{this.state.message}</div> : this.props.children;
  }
}

const state = vi.hoisted(() => ({
  user: undefined as AuthUser | undefined,
  isAuthenticated: false,
  localAdministratorOnly: false,
  standardCrudTargetKey: undefined as string | undefined,
  standardCrudDetailViewKey: undefined as string | undefined,
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
vi.mock('../pages/StandardListPageHost', () => ({
  default: ({
    viewKey,
    registration,
  }: {
    viewKey: string;
    registration?: { resourceKey?: string };
  }) => <div>{`list ${viewKey} ${registration?.resourceKey ?? 'missing'} ${window.location.search}`}</div>,
}));
vi.mock('../pages/StandardCrudDetailPageHost', () => ({
  default: ({
    viewKey,
    id,
    registration,
  }: {
    viewKey: string;
    id: string | null;
    registration?: { resourceKey?: string };
  }) => (
    <div>{`detail ${viewKey} ${id ?? 'missing'}${registration?.resourceKey ? ` ${registration.resourceKey}` : ''} ${window.location.search}`}</div>
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
        items: state.standardCrudTargetKey ? [{
          key: `${state.standardCrudTargetKey}.center`,
          path: '/records',
          entryType: 'standard-list',
          targetKey: state.standardCrudTargetKey,
          permissions: ['Example.View'],
        }] : [{
          key: 'example.page',
          path: '/example',
          entryType: 'custom',
          targetKey: 'example.page',
          permissions: ['Example.View'],
          ...(state.localAdministratorOnly ? { localAdministratorOnly: true } : {}),
        }],
      },
    }],
    viewDocuments: state.standardCrudTargetKey ? [{
      type: 'view',
      view: {
        key: state.standardCrudTargetKey,
        mode: 'list',
        ...(state.standardCrudDetailViewKey
          ? { detailViewKey: state.standardCrudDetailViewKey }
          : {}),
      },
    }, ...(state.standardCrudDetailViewKey ? [{
      type: 'view' as const,
      view: {
        key: state.standardCrudDetailViewKey,
        mode: 'detail',
      },
    }] : [])] : [],
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
    state.standardCrudTargetKey = undefined;
    state.standardCrudDetailViewKey = undefined;
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

  function registerOrganization(snapshotId: string) {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1.0.0' },
      standardCrudCapabilities: organizationBindings.standardCrudCapabilities,
    }], snapshotId);
  }

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

  it('does not load the real Personnel list implementation for an unauthorized direct route', async () => {
    window.history.replaceState({}, '', '/records?keyword=alice');
    state.standardCrudTargetKey = 'organization.person.list';
    state.standardCrudDetailViewKey = 'organization.person.detail';
    state.user = { ...state.user!, permissions: [] };
    const listBinding = organizationBindings.standardCrudCapabilities?.find(
      (binding) => binding.kind === 'standard-list',
    );
    const listLoad = vi.spyOn(listBinding!, 'load');
    registerOrganization('snapshot-organization-list-denied');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(listLoad).not.toHaveBeenCalled();
    expect(container.textContent).not.toContain('organization.person');
    listLoad.mockRestore();
  });

  it('loads the real Personnel list implementation only after access and preserves the query', async () => {
    window.history.replaceState({}, '', '/records?keyword=alice&page=2');
    state.standardCrudTargetKey = 'organization.person.list';
    state.standardCrudDetailViewKey = 'organization.person.detail';
    const listBinding = organizationBindings.standardCrudCapabilities?.find(
      (binding) => binding.kind === 'standard-list',
    );
    const detailBinding = organizationBindings.standardCrudCapabilities?.find(
      (binding) => binding.kind === 'standard-crud-detail',
    );
    const listLoad = vi.spyOn(listBinding!, 'load');
    const detailLoad = vi.spyOn(detailBinding!, 'load');
    registerOrganization('snapshot-organization-list-authorized');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    await vi.waitFor(() => {
      expect(container.textContent).toContain(
        'list organization.person.list organization.person ?keyword=alice&page=2',
      );
    });
    expect(listLoad).toHaveBeenCalledTimes(1);
    expect(detailLoad).not.toHaveBeenCalled();
    listLoad.mockRestore();
    detailLoad.mockRestore();
  });

  it('loads the real Personnel detail implementation only for an authorized detail route', async () => {
    window.history.replaceState({}, '', '/records/person-42?tab=summary');
    state.standardCrudTargetKey = 'organization.person.list';
    state.standardCrudDetailViewKey = 'organization.person.detail';
    const listBinding = organizationBindings.standardCrudCapabilities?.find(
      (binding) => binding.kind === 'standard-list',
    );
    const detailBinding = organizationBindings.standardCrudCapabilities?.find(
      (binding) => binding.kind === 'standard-crud-detail',
    );
    const listLoad = vi.spyOn(listBinding!, 'load');
    const detailLoad = vi.spyOn(detailBinding!, 'load');
    registerOrganization('snapshot-organization-detail-authorized');

    await act(async () => {
      root = createRoot(container);
      root.render(<App />);
      await Promise.resolve();
      await Promise.resolve();
    });

    await vi.waitFor(() => {
      expect(container.textContent).toContain(
        'detail organization.person.detail person-42 organization.person ?tab=summary',
      );
    });
    expect(detailLoad).toHaveBeenCalledTimes(1);
    expect(listLoad).not.toHaveBeenCalled();
    listLoad.mockRestore();
    detailLoad.mockRestore();
  });

  it('preserves :id and query semantics for a direct standard CRUD detail route', async () => {
    window.history.replaceState({}, '', '/records/record-42?tab=summary');
    state.standardCrudTargetKey = 'records.list';
    state.standardCrudDetailViewKey = 'records.detail';
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'records', displayName: 'Records', version: '1.0.0' },
      standardCrudResources: [{
        resourceKey: 'records.resource',
        apiRoute: '/api/records',
        listViewKey: 'records.list',
        detailViewKey: 'records.detail',
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
    state.standardCrudTargetKey = 'records-redirect.list';
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'records-redirect', displayName: 'Records Redirect', version: '1.0.0' },
      standardCrudResources: [{
        resourceKey: 'records-redirect.resource',
        apiRoute: '/api/records',
        listViewKey: 'records-redirect.list',
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

  it('blocks route publication and fallback loading when standard CRUD projection is invalid', async () => {
    window.history.replaceState({}, '', '/records/item-42');
    state.standardCrudTargetKey = 'invalid.records.list';
    const load = vi.fn().mockResolvedValue({ default: () => <div>should not load</div> });
    const fallback = vi.fn(() => <div>legacy fallback</div>);
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'invalid-records', displayName: 'Invalid Records', version: '1.0.0' },
      standardCrudCapabilities: [
        { kind: 'standard-list', targetKey: 'invalid.records.list', load },
        { kind: 'standard-crud-detail', targetKey: 'invalid.records.detail', load },
      ],
      routes: [{ path: '/records/:id', component: fallback, permissions: ['Example.View'] }],
    }], 'snapshot-invalid-projection');

    await act(async () => {
      root = createRoot(container);
      root.render(<ProjectionFailureBoundary><App /></ProjectionFailureBoundary>);
      await Promise.resolve();
      await Promise.resolve();
    });

    await vi.waitFor(() => {
      expect(container.textContent).toContain(
        'MODULE_PROJECTION_FAILED: STANDARD_CRUD_DETAIL_VIEW_KEY_MISSING: invalid.records.list',
      );
    });
    expect(load).not.toHaveBeenCalled();
    expect(fallback).not.toHaveBeenCalled();
    consoleError.mockRestore();
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
