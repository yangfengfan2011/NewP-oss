import { createContext } from 'react';
import type { PageInput, PageLifecycleState, ResourceChange } from '@icp-platform/sdk';

export type CompositionChange = ResourceChange & { originTreeId?: string };
export const TreeInstanceContext = createContext<string | undefined>(undefined);
export interface CompositionRuntime {
  report: (id: string, state: PageLifecycleState | null, discard?: () => void) => void;
  leave: (proceed: () => void) => void;
  subscribe: (id: string, read: (change: CompositionChange) => Promise<void>) => () => void;
  committed: (change: CompositionChange) => Promise<void>;
}
export const CompositionContext = createContext<CompositionRuntime | null>(null);
export const EmbeddedCrudContext = createContext<{
  input: PageInput; instanceId?: string; active?: boolean; reportState: (state: PageLifecycleState, discard?: () => void) => void;
  committed: (change: CompositionChange) => Promise<void>; refreshVersion: number; close?: () => void;
} | null>(null);

/** 只有导航根页面拥有地址；嵌套 PageOutlet 清除此上下文。 */
export const PageNavigationContext = createContext<{ tabKey: string | null; selectTab: (key: string) => void } | null>(null);
export const PageActivityContext = createContext(true);
