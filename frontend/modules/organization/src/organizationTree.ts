import type { PageInputResolverBinding, TreeNodeDescriptor } from '@icp-platform/sdk';
function requireNode(context: TreeNodeDescriptor | undefined, nodeType?: string) {
  if (!context || (nodeType && context.nodeType !== nodeType) || typeof context.context?.id !== 'string'
    || typeof context.context.companyId !== 'string') throw new Error('缺少必需组织节点上下文');
  return context.context as { id: string; companyId: string; parentDepartmentId: string | null };
}
export const organizationPageInputs: PageInputResolverBinding[] = [
  { key: 'organization.department.global', pageKeys: ['platform.list'], resolve: parameters => ({ parameters, queryScope: { kind: 'global' } }) },
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
