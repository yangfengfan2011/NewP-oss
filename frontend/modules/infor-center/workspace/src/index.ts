import { normalizeStandardCrudDetail, readStandardCrudRequestError, StandardCrudRequestError, type FrontendBehaviorBindings, type PageProps } from '@icp-platform/sdk';
import { platformFetch } from '@icp-platform/core';
import { createElement } from 'react';
import { createWorkspaceTreeProvider, workspaceContentSource, workspacePageInputs } from './workspaceTree';

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
    get: async (resourceKey, id, context) => {
      const scope = context?.queryScope;
      const source = workspaceContentSource({ ...scope, modelKey: scope?.sourceModelKey, objectId: scope?.sourceObjectId });
      if (source.resourceKey !== resourceKey || source.objectId !== id) throw new StandardCrudRequestError('工作空间目标已变化。', 409);
      if (typeof scope?.sourceFeaturePosition !== 'string' || !scope.sourceFeaturePosition) throw new StandardCrudRequestError('缺少来源读取位置。', 400);
      const position = { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: source.linkIds };
      const response = await platformFetch(`/api/workspace/content/${encodeURIComponent(source.sourceLinkId)}/properties?queryScope=${encodeURIComponent(JSON.stringify(position))}`,
        { signal: context?.signal, headers: { 'X-NewP-Feature': scope.sourceFeaturePosition } });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { resourceKey: string; objectId: string; detail: unknown };
      if (payload.resourceKey !== resourceKey || payload.objectId !== id) throw new StandardCrudRequestError('工作空间返回了不同目标。', 409);
      const detail = normalizeStandardCrudDetail(payload.detail);
      return { normalized: detail, value: detail };
    } }],
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'workspace.folder.list', load: () => import('./workspaceRootResource') },
    { kind: 'standard-list', targetKey: 'workspace.references.list', load: () => import('./workspaceReferencesResource') },
  ],
};
export default workspaceBindings;
