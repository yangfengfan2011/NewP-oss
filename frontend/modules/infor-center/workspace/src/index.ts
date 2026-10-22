import { normalizeStandardCrudDetail, readStandardCrudRequestError, StandardCrudRequestError, type FrontendBehaviorBindings, type PageProps, type StandardCrudDetailReadContext } from '@icp-platform/sdk';
import { platformFetch } from '@icp-platform/core';
import { workspaceHistoryCheck } from './workspaceHistoryCheck';
import { createElement } from 'react';
import { createWorkspaceTreeProvider, workspaceContentSource, workspacePageInputs } from './workspaceTree';

/** 来源校验共享，读取功能位置由各自受控入口决定，不能在两种入口之间回退。 */
async function readProperties(resourceKey: string, id: string, context: StandardCrudDetailReadContext | undefined, featurePosition: unknown) {
  const scope = context?.queryScope;
  const source = workspaceContentSource({ ...scope, modelKey: scope?.sourceModelKey, objectId: scope?.sourceObjectId });
  if (source.resourceKey !== resourceKey || source.objectId !== id) throw new StandardCrudRequestError('工作空间目标已变化。', 409);
  if (typeof featurePosition !== 'string' || !featurePosition) throw new StandardCrudRequestError('缺少来源读取位置。', 400);
  const position = { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: source.linkIds };
  const response = await platformFetch(`/api/workspace/content/${encodeURIComponent(source.sourceLinkId)}/properties?queryScope=${encodeURIComponent(JSON.stringify(position))}`,
    { signal: context?.signal, headers: { 'X-NewP-Feature': featurePosition,
      ...(context?.dataAccess?.id ? { 'X-NewP-Data-Declaration': context.dataAccess.id } : {}) } });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  const payload = await response.json() as { resourceKey: string; objectId: string; detail: unknown };
  if (payload.resourceKey !== resourceKey || payload.objectId !== id) throw new StandardCrudRequestError('工作空间返回了不同目标。', 409);
  const detail = normalizeStandardCrudDetail(payload.detail);
  return { normalized: detail, value: detail };
}

const workspaceBindings: FrontendBehaviorBindings = {
  behaviorKey: 'workspace.frontend',
  treeProviders: [createWorkspaceTreeProvider()],
  pageInputResolvers: workspacePageInputs,
  pages: [{ pageKey: 'workspace.content.permissions', requiresRecordId: true, load: async () => {
    const { ObjectAuthorizationPage } = await import('@icp-platform/ui');
    return { default: (props: PageProps) => {
      const source = workspaceContentSource(props.input.context?.context);
      const position = { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: source.linkIds };
      return createElement(ObjectAuthorizationPage, { ...props, target: { resourceKey: source.resourceKey, id: source.objectId,
        title: '对象', titleKey: 'Workspace.Object', name: props.input.context?.label,
        basePath: `/api/workspace/content/${encodeURIComponent(source.sourceLinkId)}/permissions?queryScope=${encodeURIComponent(JSON.stringify(position))}` } });
    } };
  } }],
  standardCrudDetailReadSources: [{ key: 'workspace.content.properties', resourceKeys: ['workspace.folder', 'part.part', 'document.document'],
    navigationContributions: [{ key: 'history', labelKey: 'Workspace.History', navigationKey: 'workspace.history',
      dataActionReference: { viewKey: 'workspace.folder.list', surface: 'rowActions', actionKey: 'history' }, precheck: workspaceHistoryCheck,
      getParameters: (resourceKey, id, context) => {
        const scope = context.queryScope;
        const source = workspaceContentSource({ ...scope, modelKey: scope?.sourceModelKey, objectId: scope?.sourceObjectId });
        if (source.resourceKey !== resourceKey || source.objectId !== id) throw new StandardCrudRequestError('工作空间目标已变化。', 409);
        return { objectId: source.objectId, modelKey: source.modelKey, sourceLinkId: source.sourceLinkId,
          spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: JSON.stringify(source.linkIds) };
      } }],
    get: (resourceKey, id, context) => readProperties(resourceKey, id, context, context?.queryScope?.sourceFeaturePosition) },
    { key: 'workspace.content.receivedProperties', resourceKeys: ['workspace.folder', 'part.part', 'document.document'],
      get: (resourceKey, id, context) => readProperties(resourceKey, id, context, context?.featurePosition) }],
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'workspace.transfers.list', load: () => import('./workspaceTransferNotificationsResource') },
    { kind: 'standard-list', targetKey: 'workspace.history.list', load: async () => {
      const { createObjectHistoryResource } = await import('@icp-platform/ui');
      return { default: createObjectHistoryResource({ resourceKey: 'workspace.history', listViewKey: 'workspace.history.list',
        precheck: workspaceHistoryCheck,
        resolveTarget: scope => {
          const source = workspaceContentSource({ ...scope, modelKey: scope?.sourceModelKey, objectId: scope?.sourceObjectId });
          const position = { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: source.linkIds };
          return { resourceKey: source.resourceKey, id: source.objectId,
            basePath: `/api/workspace/content/${encodeURIComponent(source.sourceLinkId)}/history?queryScope=${encodeURIComponent(JSON.stringify(position))}` };
        } }) };
    } },
    { kind: 'standard-list', targetKey: 'workspace.folder.list', load: () => import('./workspaceRootResource') },
    { kind: 'standard-list', targetKey: 'workspace.references.list', load: () => import('./workspaceReferencesResource') },
  ],
};
export default workspaceBindings;
