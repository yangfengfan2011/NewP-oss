import type { ComponentType } from 'react';

/** 页面身份与固定配置分离；运行时节点从 context 传递。 */
export interface PageRef { pageKey: string; parameters?: Record<string, unknown>; inputResolverKey?: string }
export interface TreeNodeDescriptor {
  nodeKey: string; nodeType: string; label: string; hasChildren: boolean;
  code?: string; isEnabled?: boolean; context?: Record<string, unknown>;
}
export interface PageInput {
  parameters: Record<string, unknown>; context?: TreeNodeDescriptor;
  treeScope?: Record<string, unknown>;
  recordId?: string; queryScope?: Record<string, string | null>; fixedCreateValues?: Record<string, unknown>;
}
export interface PageLifecycleState { dirty: boolean; submitting: boolean }
export interface PageProps {
  input: PageInput;
  reportState: (state: PageLifecycleState, discard?: () => void) => void;
}
export interface PageBinding {
  pageKey: string; requiredParameters?: string[]; requiresRecordId?: boolean;
  load: () => Promise<{ default: ComponentType<PageProps> }>;
}
export interface PageInputResolverBinding {
  key: string; pageKeys: string[];
  resolve: (parameters: Record<string, unknown>, context?: TreeNodeDescriptor) => PageInput;
}
export interface ResourceChange {
  resourceKey: string; operation: 'create' | 'update' | 'delete'; id: string;
  before?: Record<string, unknown>; after?: Record<string, unknown>;
}
export interface TreeNodeLocation { nodeKey: string; parentNodeKey: string | null }
export type TreeChange = { kind: 'unrelated' } | { kind: 'unknown' } | {
  kind: 'located'; operation: ResourceChange['operation']; nodeKey: string;
  beforeLocation?: TreeNodeLocation; afterLocation?: TreeNodeLocation;
};
export interface TreeNodePathItem { nodeKey: string; label: string }
export interface TreeProviderBinding {
  key: string;
  roots: (treeScope: Record<string, unknown>, signal?: AbortSignal) => Promise<TreeNodeDescriptor[]>;
  children: (treeScope: Record<string, unknown>, nodeKey: string, signal?: AbortSignal) => Promise<TreeNodeDescriptor[]>;
  path: (treeScope: Record<string, unknown>, nodeKey: string, signal?: AbortSignal) => Promise<TreeNodePathItem[]>;
  search: (treeScope: Record<string, unknown>, keyword: string, page: number, pageSize: number, signal?: AbortSignal) => Promise<{
    items: { node: TreeNodeDescriptor; path: TreeNodePathItem[] }[]; total: number; page: number; pageSize: number;
  }>;
  resolveChange: (change: ResourceChange, treeScope: Record<string, unknown>) => Promise<TreeChange>;
}
export interface TabView {
  key: string; mode: 'tab'; dicKey: string; defaultTabKey: string;
  tabs: { key: string; titleKey: string; pageRef: PageRef }[];
}
export interface TreeView {
  key: string; mode: 'tree'; dicKey: string; providerKey: string;
  nodeTypes: { key: string; titleKey: string; pageRef: PageRef;
    contextMenu?: { key: string; labelKey: string; standardAction: 'edit'; viewKey: string; inputResolverKey: string }[] }[];
}
