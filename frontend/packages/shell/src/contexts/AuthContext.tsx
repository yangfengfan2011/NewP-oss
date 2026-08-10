import { createContext, useContext, type ReactNode } from 'react';
import { useAuthenticationState } from '@icp-platform/core';
import type { AuthContext as IAuthContext } from '@icp-platform/sdk';

const AuthContext = createContext<IAuthContext | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const authentication = useAuthenticationState();

  const hasPermission = (permission: string): boolean =>
    Boolean(authentication.user?.permissions.includes(permission));
  const hasAnyPermission = (permissions: string[]): boolean => permissions.some(hasPermission);
  const hasAllPermissions = (permissions: string[]): boolean => permissions.every(hasPermission);

  return (
    <AuthContext.Provider value={{
      ...authentication,
      hasPermission,
      hasAnyPermission,
      hasAllPermissions,
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider');
  return context;
}
