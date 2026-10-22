import { platformFetch } from '@icp-platform/core';
import { createTreeQueryTransport } from '@icp-platform/sdk';
import type { TreeNodeLocation, TreeProviderBinding, TreeView } from '@icp-platform/sdk';

export const relationTreeProviderKey = 'platform.relations';

/** 常规树共用网络与变更映射；View key 选择服务器配置，不向服务器提交字段映射。 */
export function createRelationTreeProvider(view: TreeView, featurePosition?: string): TreeProviderBinding {
  const source = view.relationSource;
  if (!source?.nodes.length && !view.linkSource) throw new Error('标准关系树缺少来源');
  const checkScope = (scope: Record<string, unknown>) => {
    if (scope.kind !== 'global' || Object.keys(scope).length !== 1) throw new Error('缺少或无效树读取范围');
  };
  const transport = createTreeQueryTransport(view.key, platformFetch, featurePosition, view.dataAccess);
  return {
    key: relationTreeProviderKey,
    ...transport,
    resolveChange: async (change, scope) => {
      if (!source) return { kind: 'unknown' };
      checkScope(scope);
      const nodes = source.nodes.filter(node => node.resourceKey === change.resourceKey);
      if (!nodes.length) return { kind: 'unrelated' };
      if (nodes.length !== 1 || !change.id) return { kind: 'unknown' };
      const node = nodes[0];
      const location = (fields: Record<string, unknown> | undefined): TreeNodeLocation | undefined => {
        if (!fields) return undefined;
        for (const parent of node.parents ?? []) {
          const value = fields[parent.field];
          if (value === undefined || (value !== null && (typeof value !== 'string' || !value))) return undefined;
          if (typeof value === 'string') return { nodeKey: `${node.key}:${change.id}`, parentNodeKey: `${parent.nodeType}:${value}` };
        }
        if (node.parents?.length && !node.rootWhenParentNull) return undefined;
        return { nodeKey: `${node.key}:${change.id}`, parentNodeKey: null };
      };
      const beforeLocation = location(change.before);
      const afterLocation = location(change.after);
      if ((change.operation !== 'create' && !beforeLocation) || (change.operation !== 'delete' && !afterLocation)) return { kind: 'unknown' };
      // 静态条件可能使节点进入或离开树；正常重读不能进入 unknown 的异常恢复路径。
      return { kind: source.nodes.some(item => item.filters?.length) ? 'reload' : 'located', operation: change.operation, nodeKey: `${node.key}:${change.id}`, beforeLocation, afterLocation };
    },
  };
}
