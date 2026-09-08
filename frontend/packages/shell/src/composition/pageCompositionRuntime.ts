import { createContext } from 'react';
import type { PageInput, PageLifecycleState, ResourceChange } from '@icp-platform/sdk';

export interface CompositionRuntime {
  report: (id: string, state: PageLifecycleState | null, discard?: () => void) => void;
  leave: (proceed: () => void) => void;
  subscribe: (id: string, read: (change: ResourceChange) => Promise<void>) => () => void;
  committed: (change: ResourceChange) => Promise<void>;
}
export const CompositionContext = createContext<CompositionRuntime | null>(null);
export const EmbeddedCrudContext = createContext<{
  input: PageInput; reportState: (state: PageLifecycleState, discard?: () => void) => void;
  committed: (change: ResourceChange) => Promise<void>; refreshVersion: number; close?: () => void;
} | null>(null);
