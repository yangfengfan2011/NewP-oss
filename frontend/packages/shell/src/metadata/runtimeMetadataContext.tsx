import { createContext, useContext, useEffect, type ReactNode } from 'react';
import type { RuntimeMetadataState } from './runtimeMetadataManifest';

const RuntimeMetadataContext = createContext<RuntimeMetadataState | null>(null);

declare global {
  interface Window {
    __ICP_RUNTIME_METADATA__?: RuntimeMetadataState;
  }
}

export interface RuntimeMetadataProviderProps {
  children: ReactNode;
  value: RuntimeMetadataState | null;
}

export function RuntimeMetadataProvider({ children, value }: RuntimeMetadataProviderProps) {
  useEffect(() => {
    window.__ICP_RUNTIME_METADATA__ = value ?? undefined;

    return () => {
      if (window.__ICP_RUNTIME_METADATA__ === value) {
        window.__ICP_RUNTIME_METADATA__ = undefined;
      }
    };
  }, [value]);

  return (
    <RuntimeMetadataContext.Provider value={value}>
      {children}
    </RuntimeMetadataContext.Provider>
  );
}

export function useRuntimeMetadata() {
  return useContext(RuntimeMetadataContext);
}
