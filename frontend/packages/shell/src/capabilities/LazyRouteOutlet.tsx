import { lazy, Suspense, useMemo, useState, type ComponentType } from 'react';
import {
  RouteCapabilityErrorBoundary,
  RouteCapabilityLoading,
} from './RouteCapabilityFeedback';
import { RouteCapabilityError } from './RouteCapabilityRuntime';

export interface LazyRouteModule {
  default: ComponentType;
}

export interface LazyRouteOutletProps {
  load: () => Promise<LazyRouteModule>;
}

/** Shell 自有非首屏页面复用 capability 的局部 loading/error/retry 语义，但不进入模块注册表。 */
export function LazyRouteOutlet({ load }: LazyRouteOutletProps) {
  const [attempt, setAttempt] = useState(0);
  const routeAttempt = useMemo(() => ({
    key: attempt,
    Page: lazy(async () => {
      try {
        return await load();
      } catch (cause) {
        throw new RouteCapabilityError('ROUTE_CAPABILITY_LOAD_FAILED', undefined, cause);
      }
    }),
  }), [attempt, load]);
  const LazyPage = routeAttempt.Page;

  return (
    <RouteCapabilityErrorBoundary
      key={routeAttempt.key}
      retry={() => setAttempt((current) => current + 1)}
    >
      <Suspense fallback={<RouteCapabilityLoading />}>
        <LazyPage />
      </Suspense>
    </RouteCapabilityErrorBoundary>
  );
}
