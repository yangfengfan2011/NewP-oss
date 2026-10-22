import { useMemo, type ReactNode } from 'react';
import { RuntimeMetadataProvider as BoundRuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { bindFormalRuntimeMetadata } from './formalRuntimeMetadataTestSupport';

export function RuntimeMetadataProvider({ value, children }: { value: RuntimeMetadataState; children: ReactNode }) {
  const bound = useMemo(() => bindFormalRuntimeMetadata(value), [value]);
  return <BoundRuntimeMetadataProvider value={bound}>{children}</BoundRuntimeMetadataProvider>;
}
