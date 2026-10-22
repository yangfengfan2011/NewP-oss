import { platformFetch } from '@icp-platform/core';
import { isStandardCrudRecord, readStandardCrudRequestError, standardCrudRequestHeaders, StandardCrudRequestError, type StandardCrudActionPrecheck } from '@icp-platform/sdk';

/** 独立入口只解析真实EID；数据动作由正式声明确定，不借用对象授权管理资格。 */
export const documentHistoryCheck: StandardCrudActionPrecheck = async context => {
  const row = isStandardCrudRecord(context.row) ? context.row : {};
  const id = context.queryScope?.historyObjectId ?? context.id ?? row.EID;
  const declaration = context.action?.dataAccess;
  if (typeof id !== 'string' || !id) throw new StandardCrudRequestError('缺少操作履历对象。', 400, {}, { reasonKey: 'ObjectHistory.TargetRequired' });
  if (!declaration?.id || !context.featurePosition) throw new StandardCrudRequestError('动作数据声明不可用。', 400);
  const operation = context.action?.key === 'history' || context.action?.key === 'document.object.history' ? 'enter'
    : context.action?.key === 'view' ? 'detail' : context.action?.key === 'output' ? 'preview' : undefined;
  if (!operation || !['enter', 'detail', 'preview'].includes(operation)) throw new StandardCrudRequestError('动作数据声明执行关系无效。', 400);
  const response = await platformFetch(`/api/document/documents/${encodeURIComponent(id)}/history/check/${operation}`,
    { signal: context.signal, headers: standardCrudRequestHeaders({ featurePosition: context.featurePosition, buttonId: context.buttonId ?? declaration.id }) });
  if (response.status === 404) throw new StandardCrudRequestError('对象不存在或不可访问。', 404, {}, { reasonKey: 'ObjectHistory.Unavailable' });
  if (!response.ok) throw await readStandardCrudRequestError(response);
};
