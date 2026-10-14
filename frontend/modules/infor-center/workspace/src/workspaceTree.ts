import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, TreeNodeNotFoundError, type PageInputResolverBinding, type TreeNodeDescriptor, type TreeProviderBinding } from '@icp-platform/sdk';

interface SpaceRoot { spaceId: string; spaceKind: 'Public' | 'Personal'; rootFolderId: string; name: string; hasChildren?: boolean }
interface Position { spaceId: string; rootFolderId: string; linkIds: string[] }
function position(key: string): Position {
  try {
    const parts = key.split(':').map(decodeURIComponent);
    if (parts.length < 2 || parts.some(part => !part)) throw new Error();
    return { spaceId: parts[0], rootFolderId: parts[1], linkIds: parts.slice(2) };
  } catch { throw new TreeNodeNotFoundError('无效工作空间出现位置'); }
}
function keyFor(value: Position): string { return [value.spaceId, value.rootFolderId, ...value.linkIds].map(encodeURIComponent).join(':'); }
function positionQuery(value: Position): string { return new URLSearchParams({ spaceId: value.spaceId, rootFolderId: value.rootFolderId, linkIds: JSON.stringify(value.linkIds) }).toString(); }
const api = '/api/workspace/spaces';
function headers(scope: Record<string, unknown>) {
  if (scope.kind !== 'currentUser' || typeof scope.featurePosition !== 'string'
    || Object.keys(scope).some(key => !['kind', 'featurePosition'].includes(key))) throw new Error('缺少工作空间当前账号范围');
  return { 'X-NewP-Feature': scope.featurePosition };
}
function node(space: SpaceRoot): TreeNodeDescriptor {
  return { nodeKey: `${encodeURIComponent(space.spaceId)}:${encodeURIComponent(space.rootFolderId)}`, nodeType: 'folder',
    label: space.name, labelKey: `Workspace.${space.spaceKind}`, hasChildren: space.hasChildren ?? false, contextMenuKeys: [],
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
      if (!spaces.some(space => space.spaceKind === 'Personal')) throw new StandardCrudRequestError('个人空间尚未初始化或不可读取，请重试或联系管理员。', 503, {}, { reasonKey: 'Workspace.PersonalUnavailable' });
      return spaces.map(node);
    },
    children: async (scope, key, signal) => {
      const parent = position(key);
      const result = await read<{ items: Array<{ linkId: string; associatedObject: { id: string }; fields: { Name: string }; hasChildren: boolean }> }>(
        `/api/workspace/content/children?${positionQuery(parent)}`, scope, signal);
      return result.items.map(row => {
        const value = { ...parent, linkIds: [...parent.linkIds, row.linkId] };
        return { nodeKey: keyFor(value), nodeType: 'folder', label: row.fields.Name, hasChildren: row.hasChildren,
          context: { ...value, id: row.associatedObject.id } };
      });
    },
    path: async (scope, key, signal) => {
      const value = position(key);
      const result = await read<{ folderIds: string[]; names: string[] }>(`/api/workspace/content/path?${positionQuery(value)}`, scope, signal);
      if (result.folderIds.length !== value.linkIds.length + 1 || result.folderIds[0] !== value.rootFolderId) throw new TreeNodeNotFoundError('工作空间出现位置已过期');
      return result.names.map((label, index) => ({ nodeKey: keyFor({ ...value, linkIds: value.linkIds.slice(0, index) }), label }));
    },
    search: async () => { throw new StandardCrudRequestError('工作空间未启用树搜索。', 400, {}, { reasonKey: 'Workspace.SearchDisabled' }); },
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
