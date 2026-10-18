import { FeaturePlacementContext } from './authorization/FeaturePlacementContext';
import { useEffect, useMemo, useState } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { EntryNavigationProvider } from './navigation/EntryNavigationContext';
import {
  AuthorizationProvider,
  FirstPasswordChangePage,
  resolveAuthenticationDestination,
} from '@icp-platform/core';
import { ModuleRegistry, ShellView, type AuthUser } from '@icp-platform/sdk';
import { RouteCapabilityOutlet } from './capabilities/RouteCapabilityOutlet';
import { AuthenticationSessionBoundary } from './components/AuthenticationSessionBoundary';
import { CurrentPermissionScope } from './components/CurrentPermissionScope';
import Layout from './components/Layout';
import { ShellThemeProvider } from './components/ShellThemeProvider';
import { shellAntdTheme } from './config/antdTheme';
import { shellUiConfig } from './config/uiConfig';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { PlatformProvider } from './contexts/PlatformContext';
import { AuthenticationFrame, LocalizedBusinessBoundary, RuntimeLocalizationProvider } from './localization/RuntimeLocalizationProvider';
import { RuntimeMetadataProvider } from './metadata/runtimeMetadataContext';
import {
  getPreloadedRuntimeMetadata,
  getFallbackRuntimeMetadata,
  loadRuntimeMetadata,
} from './metadata/runtimeMetadataLoader';
import type { RuntimeMetadataState } from './metadata/runtimeMetadataManifest';
import { useRuntimeMetadata } from './metadata/runtimeMetadataContext';
import {
  buildPageEntriesFromNavDocuments,
  filterFallbackRoutesByMetadataEntries,
  resolveRouteableMetadataEntries,
} from './navigation/pageEntryAdapter';
import {
  compileStandardCrudRouteProjection,
} from './navigation/standardCrudRouteAdapter';
import { renderPageEntry } from './navigation/pageRouteRenderer';
import { ShellRouter } from './navigation/ShellRouter';
import Home from './pages/Home';
import Login from './pages/Login';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  if (!auth.isInitialized) {
    return null;
  }

  if (!auth.isAuthenticated || !auth.user) {
    return <Navigate to="/login" replace />;
  }

  const destination = resolveAuthenticationDestination(auth.user.mustChangePassword, '/');
  return destination === '/' ? <>{children}</> : <Navigate to={destination} replace />;
}

function PasswordChangeRoute() {
  const auth = useAuth();
  if (!auth.isInitialized) return null;
  if (!auth.isAuthenticated || !auth.user) return <Navigate to="/login" replace />;
  const destination = resolveAuthenticationDestination(auth.user.mustChangePassword, '/account/change-password');
  return destination === '/account/change-password'
    ? <AuthenticationFrame><FirstPasswordChangePage auth={auth} /></AuthenticationFrame>
    : <Navigate to={destination} replace />;
}

function LocalAdministratorGuard({ children }: { children: React.ReactNode }) {
  const auth = useAuth();
  return auth.user?.isLocalAdministrator ? <>{children}</> : <Navigate to="/" replace />;
}

/** 正式路由按活动入口的功能位置建立下游上下文，页面参数不授予资格。 */
export function RouteAccess({
  children,
  localAdministratorOnly,
  featurePosition,
}: {
  children: React.ReactNode;
  localAdministratorOnly?: boolean;
  featurePosition?: string;
}) {
  const auth = useAuth();
  if (localAdministratorOnly && !auth.user?.isLocalAdministrator) return <Navigate to="/" replace />;
  if (featurePosition) return auth.user?.featurePositions?.includes(featurePosition) ? <FeaturePlacementContext.Provider value={{ entryPosition: featurePosition }}>{children}</FeaturePlacementContext.Provider> : <Navigate to="/" replace />;
  const content = <>{children}</>;
  return localAdministratorOnly
    ? <LocalAdministratorGuard>{content}</LocalAdministratorGuard>
    : content;
}

function AuthorizationGate({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  return (
    <AuthorizationProvider
      featurePositions={auth.user?.featurePositions ?? []}
      loading={!auth.isInitialized}
      refreshFeatures={auth.retrySessionValidation}
    >
      {children}
    </AuthorizationProvider>
  );
}

function AppRoutes() {
  const auth = useAuth();
  const runtimeMetadata = useRuntimeMetadata();
  // 路由接管语义统一下沉到 adapter，这里只消费结果，避免 App 自己再分叉出权限或冲突规则。
  const { metadataEntries, standardCrudDetailEntries, standardCrudDrawerRedirects, fallbackRoutes } = useMemo(() => {
    const modules = ModuleRegistry.getAllModules();
    const moduleRoutes = ModuleRegistry.getRoutes();
    const projectedEntries = runtimeMetadata
      ? buildPageEntriesFromNavDocuments(runtimeMetadata.navDocuments)
      : [];
    const routeableMetadataEntries = resolveRouteableMetadataEntries(projectedEntries, moduleRoutes);
    const metadataFilteredRoutes = filterFallbackRoutesByMetadataEntries(moduleRoutes, routeableMetadataEntries);
    const standardCrudProjection = compileStandardCrudRouteProjection(
      modules,
      runtimeMetadata?.navDocuments ?? [],
      runtimeMetadata?.viewDocuments ?? [],
      metadataFilteredRoutes,
    );

    return {
      metadataEntries: routeableMetadataEntries,
      standardCrudDetailEntries: standardCrudProjection.detailEntries,
      standardCrudDrawerRedirects: standardCrudProjection.drawerRedirects,
      fallbackRoutes: standardCrudProjection.fallbackRoutes,
    };
  }, [runtimeMetadata]);

  if (!auth.isInitialized || auth.sessionError) {
    return <AuthenticationFrame><AuthenticationSessionBoundary authentication={auth}>{null}</AuthenticationSessionBoundary></AuthenticationFrame>;
  }

  return (
    <Routes>
      <Route path="/login" element={<AuthenticationFrame><Login /></AuthenticationFrame>} />
      <Route path="/account/change-password" element={<PasswordChangeRoute />} />
      <Route
        path="/"
        element={(
          <ProtectedRoute>
            <LocalizedBusinessBoundary><Layout /></LocalizedBusinessBoundary>
          </ProtectedRoute>
        )}
      >
        <Route index element={<Home />} />
        {metadataEntries.map((entry) => (
          <Route
            key={entry.path}
            path={entry.path}
            element={(
              <RouteAccess
                featurePosition={entry.featurePosition}
                localAdministratorOnly={entry.localAdministratorOnly}
              >
                {renderPageEntry(entry)}
              </RouteAccess>
            )}
          />
        ))}
        {standardCrudDrawerRedirects.map((entry) => (
          <Route key={entry.from} path={entry.from} element={<Navigate to={entry.to} replace />} />
        ))}
        {standardCrudDetailEntries.map((entry) => (
          <Route
            key={entry.path}
            path={entry.path}
            element={(
              <RouteAccess featurePosition={entry.featurePosition}>
                <RouteCapabilityOutlet capability={{
                  kind: 'standard-crud-detail',
                  targetKey: entry.viewKey,
                }} />
              </RouteAccess>
            )}
          />
        ))}
        {fallbackRoutes.map((route) => (
          <Route
            key={route.path}
            path={route.path}
            element={(
              <RouteAccess
                featurePosition={route.featurePosition}
                localAdministratorOnly={route.localAdministratorOnly}
              >
                <RouteCapabilityOutlet capability={{
                  kind: 'custom',
                  targetKey: route.path,
                }} />
              </RouteAccess>
            )}
          />
        ))}
      </Route>
    </Routes>
  );
}

function getDefaultShellView(): ShellView {
  switch (shellUiConfig.shellExperienceMode) {
    case 'platform':
      return 'platform';
    case 'product':
    case 'hybrid':
    default:
      return 'business';
  }
}

function App({ initialUser }: { initialUser?: AuthUser }) {
  const [shellViewState, setShellViewState] = useState<ShellView>(getDefaultShellView);
  const [runtimeMetadata, setRuntimeMetadata] = useState<RuntimeMetadataState | null>(
    getPreloadedRuntimeMetadata,
  );

  const shellView = useMemo<ShellView>(() => {
    switch (shellUiConfig.shellExperienceMode) {
      case 'platform':
        return 'platform';
      case 'product':
        return 'business';
      case 'hybrid':
      default:
        return shellViewState;
    }
  }, [shellViewState]);

  const canSwitchShellView = shellUiConfig.shellExperienceMode === 'hybrid';

  const setShellView = (view: ShellView) => {
    if (!canSwitchShellView) {
      return;
    }

    setShellViewState(view);
  };

  useEffect(() => {
    if (runtimeMetadata) {
      return;
    }

    let disposed = false;

    void loadRuntimeMetadata()
      .then((metadata) => {
        if (disposed) {
          return;
        }

        setRuntimeMetadata(metadata);
      })
      .catch((error) => {
        if (disposed) {
          return;
        }

        console.warn(
          'Failed to load runtime metadata manifest, falling back to module-defined navigation.',
          error,
        );
        setRuntimeMetadata(getFallbackRuntimeMetadata());
      });

    return () => {
      disposed = true;
    };
  }, [runtimeMetadata]);

  if (!runtimeMetadata) {
    return null;
  }

  return (
    <RuntimeLocalizationProvider>
      <ShellThemeProvider theme={shellAntdTheme}>
        <ShellRouter>
          <AuthProvider initialUser={initialUser}>
            <CurrentPermissionScope>
              <PlatformProvider
                  uiConfig={shellUiConfig}
                  shellView={shellView}
                  setShellView={setShellView}
                  canSwitchShellView={canSwitchShellView}
                >
                  <RuntimeMetadataProvider value={runtimeMetadata}>
                    <AuthorizationGate>
                      <EntryNavigationProvider><AppRoutes /></EntryNavigationProvider>
                    </AuthorizationGate>
                  </RuntimeMetadataProvider>
                </PlatformProvider>
            </CurrentPermissionScope>
          </AuthProvider>
        </ShellRouter>
      </ShellThemeProvider>
    </RuntimeLocalizationProvider>
  );
}

export default App;
