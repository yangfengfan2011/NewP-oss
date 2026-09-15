import { createContext, useContext, type ReactNode } from 'react';
import { useAuthenticationState, type AuthenticationState } from '@icp-platform/core';
import type { AuthContext as IAuthContext, AuthUser } from '@icp-platform/sdk';

type ShellAuthContext = IAuthContext & Pick<AuthenticationState, 'sessionError' | 'retrySessionValidation' | 'permissionEpoch'>;

const AuthContext = createContext<ShellAuthContext | null>(null);

export function AuthProvider({ children, initialUser }: { children: ReactNode; initialUser: AuthUser | undefined }) {
  const authentication = useAuthenticationState({ user: initialUser });

  return (
    <AuthContext.Provider value={{
      ...authentication,
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useOptionalAuth() {
  return useContext(AuthContext);
}

export function useAuth() {
  const context = useOptionalAuth();
  if (!context) throw new Error('useAuth must be used within AuthProvider');
  return context;
}
