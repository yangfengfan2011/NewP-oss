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
  const routes = ModuleRegistry.getRoutes();

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
        {routes.map((route) => (
          <Route
            key={route.path}
            path={route.path}
            element={
              route.permissions && route.permissions.length > 0 ? (
                <PermissionGuard permissions={route.permissions}>
                  <route.component />
                </PermissionGuard>
              ) : (
                <route.component />
              )
            }
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

  if (!localizationManifest) {
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
            <AuthorizationGate>
              <AppRoutes />
            </AuthorizationGate>
          </PlatformProvider>
        </AuthProvider>
      </LocalizationProvider>
    </BrowserRouter>
  );
}

export default App;
