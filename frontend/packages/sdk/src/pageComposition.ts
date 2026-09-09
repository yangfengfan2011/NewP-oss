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
  /** 外层隐藏时保留实例；页面据此隐藏 Portal 等脱离 DOM 层级的呈现。 */
  active?: boolean;
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
  /** reload 保留变更身份，由服务器重新判定整棵已加载树的成员关系。 */
  kind: 'located' | 'reload'; operation: ResourceChange['operation']; nodeKey: string;
  beforeLocation?: TreeNodeLocation; afterLocation?: TreeNodeLocation;
};
/** Provider 明确目标不存在；与权限、网络及路径损坏失败区分。 */
export class TreeNodeNotFoundError extends Error {}
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
  relationSource?: RelationTreeSource;
  treeActions?: TreeActionReference[];
  nodeTypes: { key: string; titleKey: string; pageRef: PageRef;
    contextMenu?: TreeActionReference[] }[];
}
/** 标准关系树只声明模型字段和父关系，不接受 SQL 或表达式。 */
export interface RelationTreeSource {
  nodes: Array<{
    key: string; model: string; resourceKey: string; idField: string; labelField: string;
    codeField: string; sortField: string; enabledField?: string; rootWhenParentNull?: boolean;
    parents?: Array<{ nodeType: string; field: string; fallback?: boolean }>;
    filters?: Array<{ field: string; operator: '=' | 'contains' | 'isNull' | 'isNotNull'; value?: string | number | boolean }>;
    contextFields?: Record<string, string>;
  }>;
}
/** 引用已有 List View 动作；权限、确认和输入仍由资源动作声明拥有。 */
export type TreeActionReference = {
  key: string; labelKey: string; viewKey: string; inputResolverKey?: string;
} & ({ standardAction: NonNullable<import('./standardCrud').StandardCrudActionDefinition['standardAction']>; actionKey?: never }
  | { actionKey: string; standardAction?: never });
