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
  input: PageInput; instanceId?: string; active?: boolean; reportState: (state: PageLifecycleState, discard?: () => void) => void;
  committed: (change: ResourceChange) => Promise<void>; refreshVersion: number; close?: () => void;
} | null>(null);

/** 只有导航根页面拥有地址；嵌套 PageOutlet 清除此上下文。 */
export const PageNavigationContext = createContext<{ tabKey: string | null; selectTab: (key: string) => void } | null>(null);
export const PageActivityContext = createContext(true);
