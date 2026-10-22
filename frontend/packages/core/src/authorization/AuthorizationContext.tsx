import { createContext, useContext, type ReactNode } from 'react';

export interface AuthorizationContextValue {
  featurePositions: string[];
  hasFeature: (position: string) => boolean;
  loading: boolean;
  refreshFeatures: () => Promise<void>;
}
const AuthorizationContext = createContext<AuthorizationContextValue | undefined>(undefined);
export interface AuthorizationProviderProps {
  featurePositions?: string[];
  children: ReactNode;
  loading?: boolean;
  refreshFeatures?: () => Promise<void>;
}
/** 仅消费认证 owner 的功能投影，不拥有第二条主体加载或旧Permission判权路径。 */
export function AuthorizationProvider({ children, featurePositions = [], loading = false, refreshFeatures = async () => {} }: AuthorizationProviderProps) {
  return <AuthorizationContext.Provider value={{ featurePositions, hasFeature: position => featurePositions.includes(position), loading, refreshFeatures }}>{children}</AuthorizationContext.Provider>;
}
export function useAuthorization(): AuthorizationContextValue {
  const context = useContext(AuthorizationContext);
  if (!context) throw new Error('useAuthorization must be used within AuthorizationProvider');
  return context;
}
