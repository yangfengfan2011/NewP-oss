import { useMemo, useState } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthorizationProvider, LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, ShellView } from '@icp-platform/sdk';
import { PermissionGuard } from './components/PermissionGuard';
import Layout from './components/Layout';
import { shellUiConfig } from './config/uiConfig';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { PlatformProvider } from './contexts/PlatformContext';
import {
  dictionaryCatalogs,
  dictionaryLanguageConfig,
  enumerationCatalogs,
  enumerationDefinitions,
} from './localization/catalogs';
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

  return (
    <BrowserRouter>
      <LocalizationProvider
        defaultLanguage={dictionaryLanguageConfig.defaultLanguage}
        supportedLanguages={dictionaryLanguageConfig.supportedLanguages}
        catalogs={dictionaryCatalogs}
        enumerationDefinitions={enumerationDefinitions}
        enumerationCatalogs={enumerationCatalogs}
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
