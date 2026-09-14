import { useState, useEffect, useCallback, createContext, useContext, ReactNode } from 'react';

export interface Permission {
  code: string;
  name: string;
  description?: string;
  category?: string;
}

export interface AuthorizationContextValue {
  hasFeature: (position: string) => boolean;
  permissions: string[];
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasAllPermissions: (permissions: string[]) => boolean;
  loading: boolean;
  refreshPermissions: () => Promise<void>;
}

const AuthorizationContext = createContext<AuthorizationContextValue | undefined>(undefined);

export interface AuthorizationProviderProps {
  /** 仅消费认证 owner 的当前完整位置投影，缺失时不放行。 */
  featurePositions?: string[];
  children: ReactNode;
  permissions?: string[];
  loading?: boolean;
  refreshPermissions?: () => Promise<void>;
  /** @deprecated Prefer passing the authenticated subject's permissions. */
  isAuthenticated?: boolean;
  /** @deprecated Prefer passing the authenticated subject's permissions. */
  apiBaseUrl?: string;
}

export function AuthorizationProvider({
  children,
  featurePositions = [],
  permissions: controlledPermissions,
  loading: controlledLoading,
  refreshPermissions: controlledRefreshPermissions,
  isAuthenticated = false,
  apiBaseUrl = '/api',
}: AuthorizationProviderProps) {
  const isControlled = controlledPermissions !== undefined;
  const [loadedPermissions, setLoadedPermissions] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const fetchPermissions = useCallback(async () => {
    if (isControlled) {
      return;
    }

    if (!isAuthenticated) {
      setLoadedPermissions([]);
      setIsLoading(false);
      return;
    }

    try {
      setIsLoading(true);
      const response = await fetch(`${apiBaseUrl}/auth/current-user`, {
        credentials: 'include',
      });
      if (response.ok) {
        const data = await response.json();
        setLoadedPermissions(Array.isArray(data.permissions) ? data.permissions : []);
      } else {
        console.error('Failed to fetch permissions:', response.statusText);
        setLoadedPermissions([]);
      }
    } catch (error) {
      console.error('Error fetching permissions:', error);
      setLoadedPermissions([]);
    } finally {
      setIsLoading(false);
    }
  }, [apiBaseUrl, isAuthenticated, isControlled]);

  useEffect(() => {
    void fetchPermissions();
  }, [fetchPermissions]);

  // Shell 传入认证快照时走受控模式；旧调用方仍可沿用原有的独立加载行为。
  const permissions = controlledPermissions ?? loadedPermissions;
  const loading = controlledLoading ?? (isControlled ? false : isLoading);
  const refreshPermissions = controlledRefreshPermissions ?? fetchPermissions;

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
    hasFeature: position => featurePositions.includes(position),
    permissions,
    hasPermission,
    hasAnyPermission,
    hasAllPermissions,
    loading,
    refreshPermissions,
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
