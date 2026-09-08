import { platformFetch } from '@icp-platform/core';
import type { PageInputResolverBinding, TreeNodeDescriptor, TreeProviderBinding, TreeNodeLocation } from '@icp-platform/sdk';

async function read<T>(operation: string, parameters: Record<string, string | number> = {}, signal?: AbortSignal): Promise<T> {
  const query = new URLSearchParams(Object.entries(parameters).map(([key, value]) => [key, String(value)]));
  const response = await platformFetch(`/api/organization/tree/${operation}?${query}`, { signal });
  if (!response.ok) throw new Error(`组织树读取失败 (${response.status})`);
  return response.json() as Promise<T>;
}
function requireNode(context: TreeNodeDescriptor | undefined, nodeType?: string) {
  if (!context || (nodeType && context.nodeType !== nodeType) || typeof context.context?.id !== 'string'
    || typeof context.context.companyId !== 'string') throw new Error('缺少必需组织节点上下文');
  return context.context as { id: string; companyId: string; parentDepartmentId: string | null };
}
export const organizationPageInputs: PageInputResolverBinding[] = [
  { key: 'organization.department.global', pageKeys: ['platform.list'], resolve: parameters => ({ parameters, queryScope: {} }) },
  { key: 'organization.tree.global', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: { kind: 'global' } }) },
  { key: 'organization.node.detail', pageKeys: ['platform.detail', 'platform.list'], resolve: (parameters, context) => ({ parameters, context, recordId: requireNode(context).id }) },
  { key: 'organization.company.direct', pageKeys: ['platform.list'], resolve: (parameters, context) => {
    const node = requireNode(context, 'company');
    return { parameters, context, queryScope: { CompanyId: node.id, ParentDepartmentId: null }, fixedCreateValues: { CompanyId: node.id, ParentDepartmentId: null } };
  } },
  { key: 'organization.department.direct', pageKeys: ['platform.list'], resolve: (parameters, context) => {
    const node = requireNode(context, 'department');
    return { parameters, context, queryScope: { CompanyId: node.companyId, ParentDepartmentId: node.id }, fixedCreateValues: { CompanyId: node.companyId, ParentDepartmentId: node.id } };
  } },
];
function globalScope(scope: Record<string, unknown>): string {
  if (scope.kind !== 'global' || Object.keys(scope).length !== 1) throw new Error('缺少或无效组织树范围');
  return 'global';
}
function location(resourceKey: string, id: string, fields: Record<string, unknown> | undefined): TreeNodeLocation | undefined {
  if (!fields) return undefined;
  if (resourceKey === 'organization.company') return { nodeKey: `company:${id}`, parentNodeKey: null };
  if (typeof fields.CompanyId !== 'string' || !Object.prototype.hasOwnProperty.call(fields, 'ParentDepartmentId')) return undefined;
  return { nodeKey: `department:${id}`, parentNodeKey: typeof fields.ParentDepartmentId === 'string' ? `department:${fields.ParentDepartmentId}` : `company:${fields.CompanyId}` };
}
export const organizationTreeProvider: TreeProviderBinding = {
  key: 'organization.tree',
  roots: (scope, signal) => read('roots', { scope: globalScope(scope) }, signal),
  children: (scope, nodeKey, signal) => read('children', { scope: globalScope(scope), nodeKey }, signal),
  path: (scope, nodeKey, signal) => read('path', { scope: globalScope(scope), nodeKey }, signal),
  search: (scope, keyword, page, pageSize, signal) => read('search', { scope: globalScope(scope), keyword, page, pageSize }, signal),
  resolveChange: async (change, scope) => {
    globalScope(scope);
    if (change.resourceKey !== 'organization.company' && change.resourceKey !== 'organization.department') return { kind: 'unrelated' };
    const beforeLocation = location(change.resourceKey, change.id, change.before);
    const afterLocation = location(change.resourceKey, change.id, change.after);
    if ((change.operation !== 'create' && !beforeLocation) || (change.operation !== 'delete' && !afterLocation)) return { kind: 'unknown' };
    return { kind: 'located', operation: change.operation, nodeKey: (afterLocation ?? beforeLocation)!.nodeKey, beforeLocation, afterLocation };
  },
};
