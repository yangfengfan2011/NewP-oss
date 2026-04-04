import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ModuleRegistry } from '@icp-platform/sdk';
import { AuthorizationProvider } from '@icp-platform/core';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { PlatformProvider } from './contexts/PlatformContext';
import { PermissionGuard } from './components/PermissionGuard';
import Layout from './components/Layout';
import Home from './pages/Home';
import Login from './pages/Login';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  // 等待 auth 初始化完成，避免误跳转
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
      <Route path="/" element={<ProtectedRoute><Layout /></ProtectedRoute>}>
        <Route index element={<Home />} />
        {routes.map(route => (
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

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <PlatformProvider>
          <AuthorizationGate>
            <AppRoutes />
          </AuthorizationGate>
        </PlatformProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
