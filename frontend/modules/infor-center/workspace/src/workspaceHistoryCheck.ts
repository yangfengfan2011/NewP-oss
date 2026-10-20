import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, standardCrudRequestHeaders, StandardCrudRequestError, type StandardCrudActionPrecheck } from '@icp-platform/sdk';
import { workspaceContentSource } from './workspaceTree';

/** 预检保留完整出现位置；数据要求只传正式声明ID，服务端重解资源/EID。 */
export const workspaceHistoryCheck: StandardCrudActionPrecheck = async context => {
  const scope = context.queryScope;
  const row = context.row as Record<string, unknown> | undefined;
  const source = workspaceContentSource({ ...scope, sourceLinkId: scope?.sourceLinkId ?? context.id ?? row?.LID,
    modelKey: scope?.sourceModelKey ?? row?.ObjectModelKey, objectId: scope?.sourceObjectId ?? row?.ObjectId });
  const declaration = context.action?.dataAccess;
  if (!declaration?.id || !context.featurePosition) throw new StandardCrudRequestError('动作数据声明不可用。', 400);
  const operation = context.action?.key === 'history' ? 'enter' : context.action?.key === 'output' ? 'preview' : context.action?.key === 'view' ? 'detail' : undefined;
  if (!operation || !['enter', 'detail', 'preview'].includes(operation)) throw new StandardCrudRequestError('动作数据声明执行关系无效。', 400);
  const position = { spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: source.linkIds };
  const response = await platformFetch(`/api/workspace/content/${encodeURIComponent(source.sourceLinkId)}/history/check/${operation}?queryScope=${encodeURIComponent(JSON.stringify(position))}`,
    { signal: context.signal, headers: standardCrudRequestHeaders({ featurePosition: context.featurePosition, buttonId: context.buttonId ?? declaration.id }) });
  if (response.status === 404) throw new StandardCrudRequestError('对象不存在或不可访问。', 404, {}, { reasonKey: 'ObjectHistory.Unavailable' });
  if (!response.ok) throw await readStandardCrudRequestError(response);
};
