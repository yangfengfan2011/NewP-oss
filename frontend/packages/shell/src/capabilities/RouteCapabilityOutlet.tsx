import { lazy, Suspense, useMemo, useState } from 'react';
import type { RouteCapabilityRef } from '@icp-platform/sdk';
import {
  RouteCapabilityErrorBoundary,
  RouteCapabilityLoading,
} from './RouteCapabilityFeedback';
import { routeCapabilityRuntime } from './RouteCapabilityRuntime';

export interface RouteCapabilityOutletProps {
  capability: RouteCapabilityRef;
}

export function RouteCapabilityOutlet({ capability }: RouteCapabilityOutletProps) {
  const [attempt, setAttempt] = useState(0);
  const { kind, targetKey } = capability;
  const LazyCapability = useMemo(() => {
    // attempt 是 React.lazy 缓存身份的一部分；retry 必须创建新缓存，runtime ref 仍只含 kind + targetKey。
    const request = { ref: { kind, targetKey }, attempt };
    return lazy(async () => ({
      default: await routeCapabilityRuntime.load(request.ref),
    }));
  }, [attempt, kind, targetKey]);
  const retry = () => setAttempt((current) => current + 1);

  return (
    <RouteCapabilityErrorBoundary
      key={`${kind}:${targetKey}:${attempt}`}
      retry={retry}
    >
      <Suspense fallback={<RouteCapabilityLoading />}>
        <LazyCapability />
      </Suspense>
    </RouteCapabilityErrorBoundary>
  );
}
