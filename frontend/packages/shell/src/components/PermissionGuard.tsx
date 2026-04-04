import { ReactNode } from 'react';
import { useAuth } from '../contexts/AuthContext';

interface PermissionGuardProps {
  children: ReactNode;
  permissions?: string[];
  requireAll?: boolean;
  fallback?: ReactNode;
}

export function PermissionGuard({
  children,
  permissions = [],
  requireAll = false,
  fallback = <div>无权限访问</div>
}: PermissionGuardProps) {
  const auth = useAuth();

  if (!auth.isAuthenticated) {
    return <>{fallback}</>;
  }

  if (permissions.length === 0) {
    return <>{children}</>;
  }

  const hasAccess = requireAll
    ? auth.hasAllPermissions(permissions)
    : auth.hasAnyPermission(permissions);

  return hasAccess ? <>{children}</> : <>{fallback}</>;
}
