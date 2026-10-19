import { readStandardCrudRequestError } from './standardCrudRest';
import { TreeNodeNotFoundError, type TreeProviderBinding } from './pageComposition';
import type { ActionDataAccessRequirement } from './standardCrud.types';

/** 正式树协议只发送范围与查询值；HTTP 实现由调用方显式提供。 */
export function createTreeQueryTransport(viewKey: string, fetcher: typeof fetch, featurePosition?: string,
  dataAccess?: ActionDataAccessRequirement): Omit<TreeProviderBinding, 'key' | 'resolveChange'> {
  if (dataAccess && !dataAccess.id) throw new Error('树读取缺少正式数据声明');
  const read = async <T,>(operation: string, scope: Record<string, unknown>, parameters: Record<string, string | number>, signal?: AbortSignal): Promise<T> => {
    if (!['global', 'currentUser'].includes(String(scope.kind)) || Object.keys(scope).some(key => !['kind', 'featurePosition'].includes(key))
      || (scope.featurePosition !== undefined && typeof scope.featurePosition !== 'string')) throw new Error('缺少或无效树读取范围');
    const placement = featurePosition ?? scope.featurePosition as string | undefined;
    const query = new URLSearchParams({ scope: String(scope.kind), ...Object.fromEntries(Object.entries(parameters).map(([key, value]) => [key, String(value)])) });
    const response = await fetcher(`/api/trees/${encodeURIComponent(viewKey)}/${operation}?${query}`, { signal, ...(placement || dataAccess ? { headers: {
      ...(placement ? { 'X-NewP-Feature': placement } : {}),
      ...(dataAccess ? { 'X-NewP-Data-Declaration': dataAccess.id } : {}),
    } } : {}) });
    if (response.status === 404) throw new TreeNodeNotFoundError('树节点已不存在');
    if (!response.ok) throw await readStandardCrudRequestError(response);
    return response.json() as Promise<T>;
  };
  return {
    roots: (scope, signal) => read('roots', scope, {}, signal),
    children: (scope, nodeKey, signal) => read('children', scope, { nodeKey }, signal),
    path: (scope, nodeKey, signal) => read('path', scope, { nodeKey }, signal),
    search: (scope, keyword, page, pageSize, signal) => read('search', scope, { keyword, page, pageSize }, signal),
  };
}
