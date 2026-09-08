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
export interface TreeLocation { nodeKey: string | null; nodeTab: string | null }
export const PageNavigationContext = createContext<{ tabKey: string | null; selectTab: (key: string, treeLocation?: TreeLocation) => void } | null>(null);
export interface TreeNavigation extends TreeLocation { locate: (node: string | null, tab?: string | null) => void }
/** 导航入口显式交给直属树；节点内的其他组合不共享该地址槽。 */
export const TreeNavigationContext = createContext<TreeNavigation | null>(null);
export const PageActivityContext = createContext(true);
