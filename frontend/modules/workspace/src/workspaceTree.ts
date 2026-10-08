import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, TreeNodeNotFoundError, type PageInputResolverBinding, type TreeNodeDescriptor, type TreeProviderBinding } from '@icp-platform/sdk';

interface SpaceRoot { spaceId: string; spaceKind: 'Public' | 'Personal'; rootFolderId: string; name: string }
const api = '/api/workspace/spaces';
function headers(scope: Record<string, unknown>) {
  if (scope.kind !== 'currentUser' || typeof scope.featurePosition !== 'string'
    || Object.keys(scope).some(key => !['kind', 'featurePosition'].includes(key))) throw new Error('缺少工作空间当前账号范围');
  return { 'X-NewP-Feature': scope.featurePosition };
}
function node(space: SpaceRoot): TreeNodeDescriptor {
  return { nodeKey: `${encodeURIComponent(space.spaceId)}:${encodeURIComponent(space.rootFolderId)}`, nodeType: 'folder',
    label: space.name, labelKey: `Workspace.${space.spaceKind}`, hasChildren: false,
    context: { spaceId: space.spaceId, rootFolderId: space.rootFolderId, id: space.rootFolderId, linkIds: [] } };
}

/** 准备状态只归本次页面范围；成功后的读取重试不会重放个人空间命令。 */
export function createWorkspaceTreeProvider(fetcher: typeof fetch = platformFetch): TreeProviderBinding {
  const prepared = new WeakMap<Record<string, unknown>, Promise<void>>();
  const read = async <T,>(path: string, scope: Record<string, unknown>, signal?: AbortSignal): Promise<T> => {
    const response = await fetcher(path, { headers: headers(scope), signal });
    if (response.status === 404) throw new TreeNodeNotFoundError('工作空间出现位置已不存在');
    if (!response.ok) throw await readStandardCrudRequestError(response);
    return response.json() as Promise<T>;
  };
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
      const spaces = await read<SpaceRoot[]>(api, scope, signal);
      if (!spaces.some(space => space.spaceKind === 'Public')) throw new StandardCrudRequestError('公共空间尚未初始化或不可读取。', 503, {}, { reasonKey: 'Workspace.PublicUnavailable' });
      return spaces.map(node);
    },
    children: async () => { throw new Error('工作空间子内容尚未接入'); },
    path: async (scope, key, signal) => {
      const parts = key.split(':');
      if (parts.length !== 2 || parts.some(part => !part)) throw new TreeNodeNotFoundError('无效工作空间出现位置');
      const [spaceId, rootId] = parts.map(decodeURIComponent);
      const detail = await read<{ id: string; fields: { ENAME: string } }>(`${api}/${encodeURIComponent(spaceId)}/root`, scope, signal);
      if (detail.id !== rootId) throw new TreeNodeNotFoundError('工作空间出现位置已过期');
      return [{ nodeKey: key, label: detail.fields.ENAME }];
    },
    search: async () => { throw new StandardCrudRequestError('工作空间未启用树搜索。', 400, {}, { reasonKey: 'Workspace.SearchDisabled' }); },
    resolveChange: async () => ({ kind: 'unrelated' }),
  };
}

export const workspacePageInputs: PageInputResolverBinding[] = [
  { key: 'workspace.tree.current', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters,
    treeScope: { kind: 'currentUser', featurePosition: parameters.featurePosition } }) },
  { key: 'workspace.root.detail', pageKeys: ['platform.detail'], resolve: (parameters, context) => {
    if (context?.nodeType !== 'folder' || typeof context.context?.spaceId !== 'string'
      || typeof context.context.rootFolderId !== 'string' || context.context.id !== context.context.rootFolderId
      || !Array.isArray(context.context.linkIds) || context.context.linkIds.length !== 0) throw new Error('缺少工作空间根上下文');
    return { parameters, context, recordId: context.context.rootFolderId };
  } },
];
