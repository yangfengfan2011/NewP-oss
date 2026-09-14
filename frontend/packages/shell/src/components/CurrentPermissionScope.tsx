import { useEffect, useRef, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import { useAuth } from '../contexts/AuthContext';

/** 当前主体拥有查询缓存；导航重取权限，明确拒绝或权限变化丢弃旧查询。 */
export function CurrentPermissionScope({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const { isAuthenticated, retrySessionValidation } = auth;
  const { currentLanguage } = useLocalization();
  const { pathname } = useLocation();
  const previousPath = useRef(pathname);
  useEffect(() => {
    if (previousPath.current === pathname) return;
    previousPath.current = pathname;
    if (isAuthenticated) void retrySessionValidation().catch(() => undefined);
  }, [pathname, isAuthenticated, retrySessionValidation]);
  const scopeKey = JSON.stringify([auth.user?.id, currentLanguage, auth.permissionEpoch, [...(auth.user?.permissions ?? [])].sort(), [...(auth.user?.featurePositions ?? [])].sort()]);
  return <PlatformQueryProvider scopeKey={scopeKey}>{children}</PlatformQueryProvider>;
}
