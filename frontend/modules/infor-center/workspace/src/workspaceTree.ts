import { platformFetch } from '@icp-platform/core';
import { createTreeQueryTransport, readStandardCrudRequestError, StandardCrudRequestError, type PageInputResolverBinding, type TreeProviderBinding, type TreeReadContext } from '@icp-platform/sdk';

const objectViews: Record<string, { resourceKey: string; viewKey: string }> = {
  WorkspaceFolder: { resourceKey: 'workspace.folder', viewKey: 'workspace.folder.detail' },
  Part: { resourceKey: 'part.part', viewKey: 'part.part.detail' },
  Document: { resourceKey: 'document.document', viewKey: 'document.document.detail' },
};

/** 对象身份与出现位置分别保留；LID 只作为读取来源，不作为授权目标。 */
export function workspaceContentSource(value: Record<string, unknown> | undefined) {
  const model = typeof value?.modelKey === 'string' ? objectViews[value.modelKey] : undefined;
  const linkIds: unknown = typeof value?.linkIds === 'string' ? JSON.parse(value.linkIds) : value?.linkIds;
  if (!model || typeof value?.objectId !== 'string' || !value.objectId || typeof value.sourceLinkId !== 'string' || !value.sourceLinkId
    || typeof value.spaceId !== 'string' || !value.spaceId || typeof value.rootFolderId !== 'string' || !value.rootFolderId
    || !Array.isArray(linkIds) || linkIds.some(id => typeof id !== 'string' || !id)) throw new Error('缺少对象及完整工作空间来源');
  return { ...model, objectId: value.objectId, modelKey: String(value.modelKey), sourceLinkId: value.sourceLinkId,
    spaceId: value.spaceId, rootFolderId: value.rootFolderId, linkIds: linkIds as string[] };
}

const api = '/api/workspace/spaces';
function headers(scope: Record<string, unknown>, featurePosition?: string) {
  if (scope.kind !== 'currentUser' || typeof scope.featurePosition !== 'string'
    || Object.keys(scope).some(key => !['kind', 'featurePosition'].includes(key))) throw new Error('缺少工作空间当前账号范围');
  return { 'X-NewP-Feature': featurePosition ?? scope.featurePosition };
}
/** 准备状态只归本次页面范围；成功后的读取重试不会重放个人空间命令。 */
export function createWorkspaceTreeProvider(fetcher: typeof fetch = platformFetch, readContext?: TreeReadContext): TreeProviderBinding {
  const prepared = new WeakMap<Record<string, unknown>, Promise<void>>();
  const transport = readContext?.dataAccess?.id && readContext.featurePosition
    ? createTreeQueryTransport('workspace.structure', fetcher, readContext.featurePosition, readContext.dataAccess) : undefined;
  const query = () => {
    if (!transport) throw new StandardCrudRequestError('工作空间树缺少当前页面正式读取声明。', 400);
    return transport;
  };
  const prepare = (scope: Record<string, unknown>) => {
    let pending = prepared.get(scope);
    if (!pending) {
      // 显式进入命令不随树读取取消；离开后仍不会把其结果应用到另一个主体或重放普通创建。
      pending = fetcher(`${api}/personal/ensure`, { method: 'POST', headers: headers(scope, readContext?.featurePosition) }).then(async response => {
        if (!response.ok) throw await readStandardCrudRequestError(response);
      }).catch(error => { prepared.delete(scope); throw error; });
      prepared.set(scope, pending);
    }
    return pending;
  };
  return {
    key: 'workspace.spaces',
    bindRead: context => createWorkspaceTreeProvider(fetcher, context),
    roots: async (scope, signal) => {
      const current = query();
      await prepare(scope);
      signal?.throwIfAborted();
      const roots = await current.roots(scope, signal);
      if (!roots.some(root => root.labelKey === 'Workspace.Public')) throw new StandardCrudRequestError('公共空间尚未初始化或不可读取。', 503, {}, { reasonKey: 'Workspace.PublicUnavailable' });
      if (!roots.some(root => root.labelKey === 'Workspace.Personal')) throw new StandardCrudRequestError('个人空间尚未初始化或不可读取，请重试或联系管理员。', 503, {}, { reasonKey: 'Workspace.PersonalUnavailable' });
      return roots;
    },
    children: async (scope, nodeKey, signal) => query().children(scope, nodeKey, signal),
    path: async (scope, nodeKey, signal) => query().path(scope, nodeKey, signal),
    search: async (scope, keyword, page, pageSize, signal) => query().search(scope, keyword, page, pageSize, signal),
    resolveChange: async change => ({ kind: change.resourceKey === 'workspace.folder' || change.resourceKey === 'workspace.content' ? 'unknown' : 'unrelated' }),
  };
}

export const workspacePageInputs: PageInputResolverBinding[] = [
  { key: 'workspace.content.receivedObject', pageKeys: ['platform.detail'], resolve: parameters => {
    const source = workspaceContentSource(parameters);
    return { parameters: { ...parameters, viewKey: source.viewKey, detailReadSourceKey: 'workspace.content.receivedProperties' },
      recordId: source.objectId, queryScope: { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: JSON.stringify(source.linkIds),
        sourceLinkId: source.sourceLinkId, sourceObjectId: source.objectId, sourceModelKey: source.modelKey } };
  } },
  { key: 'workspace.content.historyInput', pageKeys: ['platform.list'], resolve: (parameters, context) => {
    const source = workspaceContentSource({ ...context?.context, ...parameters });
    // history 头由 PageOutlet 的实际声明位置提供；来源 read 位置不能充当履历资格。
    return { parameters: { ...parameters, viewKey: 'workspace.history.list' }, context, recordId: source.objectId,
      queryScope: { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: JSON.stringify(source.linkIds),
        sourceLinkId: source.sourceLinkId, sourceObjectId: source.objectId, sourceModelKey: source.modelKey } };
  } },
  { key: 'workspace.content.record', pageKeys: ['platform.tabs', 'workspace.content.permissions'], resolve: (parameters, context) => {
    const source = workspaceContentSource(context?.context ?? parameters);
    const readPosition = parameters.sourceFeaturePosition ?? context?.context?.sourceFeaturePosition;
    if (typeof readPosition !== 'string' || !readPosition) throw new Error('缺少来源读取位置');
    const selected = { ...(context ?? { nodeKey: source.sourceLinkId, nodeType: 'content', label: String(parameters.name ?? ''), hasChildren: false }),
      context: { ...source, sourceFeaturePosition: readPosition } };
    return { parameters: { ...parameters, sourceFeaturePosition: readPosition }, context: selected, recordId: source.objectId,
      queryScope: { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: JSON.stringify(source.linkIds),
        sourceLinkId: source.sourceLinkId, sourceObjectId: source.objectId, sourceModelKey: source.modelKey, sourceFeaturePosition: readPosition } };
  } },
  { key: 'workspace.content.propertiesInput', pageKeys: ['platform.detail'], resolve: (parameters, context) => {
    const source = workspaceContentSource(context?.context);
    const readPosition = context?.context?.sourceFeaturePosition;
    if (typeof readPosition !== 'string' || !readPosition) throw new Error('缺少来源读取位置');
    return { parameters: { ...parameters, viewKey: source.viewKey, detailReadSourceKey: 'workspace.content.properties' }, context,
      recordId: source.objectId, queryScope: { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: JSON.stringify(source.linkIds),
        sourceLinkId: source.sourceLinkId, sourceObjectId: source.objectId, sourceModelKey: source.modelKey, sourceFeaturePosition: readPosition } };
  } },
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
