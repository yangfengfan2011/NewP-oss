import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionPrecheck } from '@icp-platform/sdk';

/** 独立履历只采用当前EID和自身声明，不借Workspace出现位置或其他功能资格。 */
export const partHistoryCheck: StandardCrudActionPrecheck = async context => {
  const id = context.queryScope?.historyObjectId ?? context.id;
  const declaration = context.action?.dataAccess;
  if (typeof id !== 'string' || !id || !declaration?.id || !context.featurePosition)
    throw new StandardCrudRequestError('动作数据声明或对象不可用。', 400);
  const execution = declaration.execution.split('.');
  const operation = execution[execution.length - 1];
  if (!operation || !['enter', 'detail', 'preview'].includes(operation)) throw new StandardCrudRequestError('动作数据声明执行关系无效。', 400);
  const response = await platformFetch(`/api/part/parts/${encodeURIComponent(id)}/history/check/${operation}`,
    { signal: context.signal, headers: { 'X-NewP-Feature': context.featurePosition, 'X-NewP-Data-Declaration': declaration.id } });
  if (response.status === 404) throw new StandardCrudRequestError('对象不存在或不可访问。', 404, {}, { reasonKey: 'ObjectHistory.Unavailable' });
  if (!response.ok) throw await readStandardCrudRequestError(response);
};
