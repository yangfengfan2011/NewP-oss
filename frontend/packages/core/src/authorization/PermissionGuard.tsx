import { ReactNode } from 'react';
import { usePermission, useAnyPermission, useAllPermissions } from './AuthorizationContext';

export interface PermissionGuardProps {
  children: ReactNode;
  permission?: string;
  anyPermissions?: string[];
  allPermissions?: string[];
  fallback?: ReactNode;
}

export function PermissionGuard({
  children,
  permission,
  anyPermissions,
  allPermissions,
  fallback = null
}: PermissionGuardProps) {
  const hasSinglePermission = usePermission(permission || '');
  const hasAny = useAnyPermission(anyPermissions || []);
  const hasAll = useAllPermissions(allPermissions || []);

  let hasAccess = true;

  if (permission) {
    hasAccess = hasAccess && hasSinglePermission;
  }

  if (anyPermissions && anyPermissions.length > 0) {
    hasAccess = hasAccess && hasAny;
  }

  if (allPermissions && allPermissions.length > 0) {
    hasAccess = hasAccess && hasAll;
  }

  if (!hasAccess) {
    return <>{fallback}</>;
  }

  return <>{children}</>;
}

export interface PermissionButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  permission?: string;
  anyPermissions?: string[];
  allPermissions?: string[];
}

export function PermissionButton({
  permission,
  anyPermissions,
  allPermissions,
  disabled,
  ...props
}: PermissionButtonProps) {
  const hasSinglePermission = usePermission(permission || '');
  const hasAny = useAnyPermission(anyPermissions || []);
  const hasAll = useAllPermissions(allPermissions || []);

  let hasAccess = true;

  if (permission) {
    hasAccess = hasAccess && hasSinglePermission;
  }

  if (anyPermissions && anyPermissions.length > 0) {
    hasAccess = hasAccess && hasAny;
  }

  if (allPermissions && allPermissions.length > 0) {
    hasAccess = hasAccess && hasAll;
  }

  return <button {...props} disabled={disabled || !hasAccess} />;
}
