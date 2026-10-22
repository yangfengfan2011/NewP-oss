import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import {
  FirstPasswordChangePage,
  resolveAuthenticationDestination,
} from '@icp-platform/core';
import type { AuthUser } from '@icp-platform/sdk';
import { AuthenticationSessionBoundary } from './components/AuthenticationSessionBoundary';
import { ShellThemeProvider } from './components/ShellThemeProvider';
import { shellAntdTheme } from './config/antdTheme';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import Login from './pages/Login';
import { AuthenticationFrame, RuntimeLocalizationProvider } from './localization/RuntimeLocalizationProvider';

function PasswordChangeRoute() {
  const auth = useAuth();
  if (!auth.isInitialized) return null;
  if (!auth.isAuthenticated || !auth.user) return <Navigate to="/login" replace />;
  const destination = resolveAuthenticationDestination(auth.user.mustChangePassword, '/account/change-password');
  return destination === '/account/change-password'
    ? <FirstPasswordChangePage auth={auth} />
    : <Navigate to={destination} replace />;
}

function AuthenticationOnlyRoutes() {
  const auth = useAuth();
  if (!auth.isInitialized || auth.sessionError) {
    return <AuthenticationSessionBoundary authentication={auth}>{null}</AuthenticationSessionBoundary>;
  }
  if (!auth.isAuthenticated || !auth.user) {
    return (
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    );
  }
  if (!auth.user.mustChangePassword) {
    return null;
  }

  return (
    <Routes>
      <Route path="/account/change-password" element={<PasswordChangeRoute />} />
      <Route path="*" element={<Navigate to="/account/change-password" replace />} />
    </Routes>
  );
}

/** 匿名与受限会话只装配认证所需依赖，业务 Shell 由 bootstrap 在鉴权后另行导入。 */
export function AuthenticationOnlyApp({ initialUser }: { initialUser?: AuthUser }) {
  return (
    <RuntimeLocalizationProvider>
      <ShellThemeProvider theme={shellAntdTheme}>
        <BrowserRouter>
          <AuthProvider initialUser={initialUser}>
            <AuthenticationFrame><AuthenticationOnlyRoutes /></AuthenticationFrame>
          </AuthProvider>
        </BrowserRouter>
      </ShellThemeProvider>
    </RuntimeLocalizationProvider>
  );
}
