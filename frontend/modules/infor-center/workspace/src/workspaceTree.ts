import { platformFetch } from '@icp-platform/core';
import { createTreeQueryTransport, readStandardCrudRequestError, StandardCrudRequestError, type PageInputResolverBinding, type TreeProviderBinding } from '@icp-platform/sdk';

const api = '/api/workspace/spaces';
function headers(scope: Record<string, unknown>) {
  if (scope.kind !== 'currentUser' || typeof scope.featurePosition !== 'string'
    || Object.keys(scope).some(key => !['kind', 'featurePosition'].includes(key))) throw new Error('缺少工作空间当前账号范围');
  return { 'X-NewP-Feature': scope.featurePosition };
}
/** 准备状态只归本次页面范围；成功后的读取重试不会重放个人空间命令。 */
export function createWorkspaceTreeProvider(fetcher: typeof fetch = platformFetch): TreeProviderBinding {
  const prepared = new WeakMap<Record<string, unknown>, Promise<void>>();
  const query = createTreeQueryTransport('workspace.structure', fetcher);
  const prepare = (scope: Record<string, unknown>) => {
    let pending = prepared.get(scope);
    if (!pending) {
      // 显式进入命令不随树读取取消；离开后仍不会把其结果应用到另一个主体或重放普通创建。
      pending = fetcher(`${api}/personal/ensure`, { method: 'POST', headers: headers(scope) }).then(async response => {
        if (!response.ok) throw await readStandardCrudRequestError(response);
      }).catch(error => { prepared.delete(scope); throw error; });
      prepared.set(scope, pending);
    }
    return pending;
  };
  return {
    key: 'workspace.spaces',
    roots: async (scope, signal) => {
      await prepare(scope);
      signal?.throwIfAborted();
      const roots = await query.roots(scope, signal);
      if (!roots.some(root => root.labelKey === 'Workspace.Public')) throw new StandardCrudRequestError('公共空间尚未初始化或不可读取。', 503, {}, { reasonKey: 'Workspace.PublicUnavailable' });
      if (!roots.some(root => root.labelKey === 'Workspace.Personal')) throw new StandardCrudRequestError('个人空间尚未初始化或不可读取，请重试或联系管理员。', 503, {}, { reasonKey: 'Workspace.PersonalUnavailable' });
      return roots;
    },
    children: query.children,
    path: query.path,
    search: query.search,
    resolveChange: async change => ({ kind: change.resourceKey === 'workspace.folder' || change.resourceKey === 'workspace.content' ? 'unknown' : 'unrelated' }),
  };
}

export const workspacePageInputs: PageInputResolverBinding[] = [
  { key: 'workspace.folder.reference', pageKeys: ['platform.list'], resolve: (parameters, context) => {
    const value = context?.context;
    if (context?.nodeType !== 'folder' || typeof value?.id !== 'string' || typeof value.spaceId !== 'string' || typeof value.rootFolderId !== 'string'
      || !Array.isArray(value.linkIds) || value.linkIds.length === 0 || value.linkIds.some(id => typeof id !== 'string' || !id)) {
      throw new Error('缺少普通文件夹引用位置');
    }
    const linkIds = value.linkIds as string[];
    return { parameters, context, recordId: linkIds[linkIds.length - 1],
      queryScope: { spaceId: value.spaceId, rootFolderId: value.rootFolderId, linkIds: JSON.stringify(linkIds.slice(0, -1)), referenceObjectId: value.id } };
  } },
  { key: 'workspace.references.object', pageKeys: ['platform.list'], resolve: parameters => {
    if (typeof parameters.recordId !== 'string' || !parameters.recordId || typeof parameters.anchorModelKey !== 'string') throw new Error('缺少直接引用对象');
    return { parameters, queryScope: { anchorId: parameters.recordId, anchorModelKey: parameters.anchorModelKey } };
  } },
  { key: 'workspace.tree.current', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters,
    treeScope: { kind: 'currentUser', featurePosition: parameters.featurePosition } }) },
  { key: 'workspace.folder.content', pageKeys: ['platform.list'], resolve: (parameters, context) => {
    if (context?.nodeType !== 'folder' || typeof context.context?.spaceId !== 'string'
      || typeof context.context.rootFolderId !== 'string' || typeof context.context.id !== 'string'
      || !Array.isArray(context.context.linkIds) || context.context.linkIds.some(id => typeof id !== 'string')) throw new Error('缺少工作空间出现位置');
    const value = { spaceId: context.context.spaceId, rootFolderId: context.context.rootFolderId, linkIds: context.context.linkIds as string[] };
    return { parameters, context, queryScope: { spaceId: value.spaceId, rootFolderId: value.rootFolderId, linkIds: JSON.stringify(value.linkIds), anchorId: context.context.id },
      fixedCreateValues: { WorkspacePosition: value } };
  } },
];
