import { useEffect, useMemo, useState } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthorizationProvider, LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, ShellView } from '@icp-platform/sdk';
import { PermissionGuard } from './components/PermissionGuard';
import Layout from './components/Layout';
import { shellUiConfig } from './config/uiConfig';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { PlatformProvider } from './contexts/PlatformContext';
import {
  getFallbackRuntimeLocalizationManifest,
  loadRuntimeLocalizationManifest,
  type RuntimeLocalizationManifest,
} from './localization/runtimeLocalizationManifest';
import { RuntimeMetadataProvider } from './metadata/runtimeMetadataContext';
import {
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
import { renderPageEntry } from './navigation/pageRouteRenderer';
import Home from './pages/Home';
import Login from './pages/Login';
import UiShowcase from './pages/UiShowcase';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  if (!auth.isInitialized) {
    return null;
  }

  return auth.isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />;
}

function AuthorizationGate({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  return <AuthorizationProvider userId={auth.user?.id}>{children}</AuthorizationProvider>;
}

function AppRoutes() {
  const runtimeMetadata = useRuntimeMetadata();
  // 路由接管语义统一下沉到 adapter，这里只消费结果，避免 App 自己再分叉出权限或冲突规则。
  const { metadataEntries, fallbackRoutes } = useMemo(() => {
    const moduleRoutes = ModuleRegistry.getRoutes();
    const projectedEntries = runtimeMetadata
      ? buildPageEntriesFromNavDocuments(runtimeMetadata.navDocuments)
      : [];
    const routeableMetadataEntries = resolveRouteableMetadataEntries(projectedEntries, moduleRoutes);

    return {
      metadataEntries: routeableMetadataEntries,
      fallbackRoutes: filterFallbackRoutesByMetadataEntries(moduleRoutes, routeableMetadataEntries),
    };
  }, [runtimeMetadata]);

  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        path="/"
        element={(
          <ProtectedRoute>
            <Layout />
          </ProtectedRoute>
        )}
      >
        <Route index element={<Home />} />
        <Route path="ui-showcase" element={<UiShowcase />} />
        {metadataEntries.map((entry) => (
          <Route
            key={entry.path}
            path={entry.path}
            element={entry.permissions && entry.permissions.length > 0 ? (
              <PermissionGuard permissions={entry.permissions}>
                {renderPageEntry(entry)}
              </PermissionGuard>
            ) : (
              renderPageEntry(entry)
            )}
          />
        ))}
        {fallbackRoutes.map((route) => (
          <Route
            key={route.path}
            path={route.path}
            element={route.permissions && route.permissions.length > 0 ? (
              <PermissionGuard permissions={route.permissions}>
                <route.component />
              </PermissionGuard>
            ) : (
              <route.component />
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

function App() {
  const [shellViewState, setShellViewState] = useState<ShellView>(getDefaultShellView);
  const [localizationManifest, setLocalizationManifest] = useState<RuntimeLocalizationManifest | null>(null);
  const [runtimeMetadata, setRuntimeMetadata] = useState<RuntimeMetadataState | null>(null);

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
    let disposed = false;

    void loadRuntimeLocalizationManifest()
      .then((manifest) => {
        if (disposed) {
          return;
        }

        setLocalizationManifest(manifest);
      })
      .catch((error) => {
        if (disposed) {
          return;
        }

        // 登录页不能因为运行时本地化清单缺失而整页白屏；
        // 这里回退到最小语言清单，同时保留告警，方便继续追宿主或运行时投影视图问题。
        console.warn(
          'Failed to load runtime localization manifest, falling back to the built-in minimal manifest.',
          error,
        );
        setLocalizationManifest(getFallbackRuntimeLocalizationManifest());
      });

    return () => {
      disposed = true;
    };
  }, []);

  useEffect(() => {
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
  }, []);

  if (!localizationManifest || !runtimeMetadata) {
    return null;
  }

  return (
    <BrowserRouter>
      <LocalizationProvider
        defaultLanguage={localizationManifest.defaultLanguage}
        supportedLanguages={localizationManifest.supportedLanguages}
        catalogs={localizationManifest.catalogs}
      >
        <AuthProvider>
          <PlatformProvider
            uiConfig={shellUiConfig}
            shellView={shellView}
            setShellView={setShellView}
            canSwitchShellView={canSwitchShellView}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <AuthorizationGate>
                <AppRoutes />
              </AuthorizationGate>
            </RuntimeMetadataProvider>
          </PlatformProvider>
        </AuthProvider>
      </LocalizationProvider>
    </BrowserRouter>
  );
}

export default App;
