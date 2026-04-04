import { useState, useEffect, createContext, useContext, ReactNode } from 'react';

export interface Permission {
  code: string;
  name: string;
  description?: string;
  category?: string;
}

export interface AuthorizationContextValue {
  permissions: string[];
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasAllPermissions: (permissions: string[]) => boolean;
  loading: boolean;
  refreshPermissions: () => Promise<void>;
}

const AuthorizationContext = createContext<AuthorizationContextValue | undefined>(undefined);

export interface AuthorizationProviderProps {
  children: ReactNode;
  userId?: string;
  apiBaseUrl?: string;
}

export function AuthorizationProvider({ children, userId, apiBaseUrl = '/api' }: AuthorizationProviderProps) {
  const [permissions, setPermissions] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchPermissions = async () => {
    if (!userId) {
      setPermissions([]);
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      const response = await fetch(`${apiBaseUrl}/authorization/users/${userId}/permissions`);
      if (response.ok) {
        const data = await response.json();
        setPermissions(data);
      } else {
        console.error('Failed to fetch permissions:', response.statusText);
        setPermissions([]);
      }
    } catch (error) {
      console.error('Error fetching permissions:', error);
      setPermissions([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchPermissions();
  }, [userId]);

  const hasPermission = (permission: string): boolean => {
    return permissions.includes(permission);
  };

  const hasAnyPermission = (perms: string[]): boolean => {
    return perms.some(p => permissions.includes(p));
  };

  const hasAllPermissions = (perms: string[]): boolean => {
    return perms.every(p => permissions.includes(p));
  };

  const value: AuthorizationContextValue = {
    permissions,
    hasPermission,
    hasAnyPermission,
    hasAllPermissions,
    loading,
    refreshPermissions: fetchPermissions
  };

  return (
    <AuthorizationContext.Provider value={value}>
      {children}
    </AuthorizationContext.Provider>
  );
}

export function useAuthorization(): AuthorizationContextValue {
  const context = useContext(AuthorizationContext);
  if (!context) {
    throw new Error('useAuthorization must be used within AuthorizationProvider');
  }
  return context;
}

export function usePermission(permission: string): boolean {
  const { hasPermission } = useAuthorization();
  return hasPermission(permission);
}

export function useAnyPermission(permissions: string[]): boolean {
  const { hasAnyPermission } = useAuthorization();
  return hasAnyPermission(permissions);
}

export function useAllPermissions(permissions: string[]): boolean {
  const { hasAllPermissions } = useAuthorization();
  return hasAllPermissions(permissions);
}
