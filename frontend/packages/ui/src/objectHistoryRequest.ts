import { isStandardCrudRecord, readStandardCrudRequestError, StandardCrudRequestError } from '@icp-platform/sdk';

export interface ObjectHistoryTarget { resourceKey: string; id: string; basePath: string }
export interface ObjectHistoryFilter { from?: string; to?: string; actorId?: string; eventType?: string; objectType?: string; ipAddress?: string; description?: string }
export interface ObjectHistoryItem {
  id: string;
  eventType: string;
  occurredAt: string;
  objectType: string;
  actor: { name: string | null; status: 'available' | 'unavailable' };
  target: { name: string | null; code: string | null; nameSource: 'historical' | 'current' | 'unrecorded' | 'unavailable' };
  description: { key: string; parameters: Record<string, string> };
  ipAddress: string | null;
}
export interface ObjectHistoryPage { items: ObjectHistoryItem[]; total: number; pageIndex: number; pageSize: number }
export interface ObjectHistoryReadContext { featurePosition?: string; signal?: AbortSignal; dataAccess?: import('@icp-platform/sdk').ActionDataAccessRequirement }

/** 每次请求只传正式声明身份；从不携带允许结果或客户端数据动作要求。 */
function headers(context: ObjectHistoryReadContext) {
  return { 'X-NewP-Feature': context.featurePosition!, ...(context.dataAccess ? { 'X-NewP-Data-Declaration': context.dataAccess.id } : {}) };
}

function invalidResponse(): never {
  throw new StandardCrudRequestError('操作履历响应无效。', 502, {}, { reasonKey: 'ObjectHistory.InvalidResponse' });
}
function text(value: unknown): string { return typeof value === 'string' && value.length > 0 ? value : invalidResponse(); }
function nullableText(value: unknown): string | null { return value === null || typeof value === 'string' ? value : invalidResponse(); }

/** 明确复制普通 DTO 白名单；管理员 payload、隐藏关联 ID 或历史字段 JSON 不进入组件数据。 */
export function readObjectHistoryItem(value: unknown): ObjectHistoryItem {
  if (!isStandardCrudRecord(value) || !isStandardCrudRecord(value.actor) || !isStandardCrudRecord(value.target)
    || !isStandardCrudRecord(value.description) || !isStandardCrudRecord(value.description.parameters)) return invalidResponse();
  const actorStatus = value.actor.status;
  const nameSource = value.target.nameSource;
  if (actorStatus !== 'available' && actorStatus !== 'unavailable') return invalidResponse();
  if (nameSource !== 'historical' && nameSource !== 'current' && nameSource !== 'unrecorded' && nameSource !== 'unavailable') return invalidResponse();
  const parameters: Record<string, string> = {};
  for (const [key, parameter] of Object.entries(value.description.parameters)) {
    if (typeof parameter !== 'string') return invalidResponse();
    Object.defineProperty(parameters, key, { value: parameter, enumerable: true });
  }
  return { id: text(value.id), eventType: text(value.eventType), occurredAt: text(value.occurredAt), objectType: text(value.objectType),
    actor: { name: nullableText(value.actor.name), status: actorStatus },
    target: { name: nameSource === 'unavailable' ? null : nullableText(value.target.name),
      code: nameSource === 'unavailable' ? null : nullableText(value.target.code), nameSource },
    description: { key: text(value.description.key), parameters }, ipAddress: nullableText(value.ipAddress) };
}

/** 模块提供完整 owner 路径；追加记录定位及筛选时保留原来源，不提供管理员或裸对象退路。 */
function historyPath(target: ObjectHistoryTarget, filter: ObjectHistoryFilter, recordId?: string): string {
  if (!target.resourceKey || !target.id || !target.basePath?.startsWith('/') || target.basePath.startsWith('//') || recordId === '')
    throw new StandardCrudRequestError('缺少操作履历对象或来源。', 400, {}, { reasonKey: 'ObjectHistory.TargetRequired' });
  const url = new URL(target.basePath, 'http://object-history.local');
  if (recordId !== undefined) url.pathname = `${url.pathname.replace(/\/$/, '')}/${encodeURIComponent(recordId)}`;
  for (const key of ['from', 'to', 'actorId', 'eventType', 'objectType', 'ipAddress', 'description'] as const)
    if (filter[key] !== undefined && filter[key] !== '') url.searchParams.set(key, filter[key]);
  return url.pathname + url.search;
}

/** 详情每次经当前 owner/history 资格读取，取消和拒绝继续传播，不用已有列表行降级。 */
export async function readObjectHistoryDetail(target: ObjectHistoryTarget, recordId: string, filter: ObjectHistoryFilter,
  context: ObjectHistoryReadContext, fetcher: typeof fetch): Promise<ObjectHistoryItem> {
  if (!context.featurePosition?.trim())
    throw new StandardCrudRequestError('缺少操作履历功能位置。', 400, {}, { reasonKey: 'ObjectHistory.FeatureRequired' });
  const response = await fetcher(historyPath(target, filter, recordId), { method: 'GET', signal: context.signal,
    headers: headers(context) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  const item = readObjectHistoryItem(await response.json());
  if (item.id !== recordId) return invalidResponse();
  return item;
}

/** 只复制已发布筛选键，列表列投影和通用排序参数不传入 owner 的受限查询协议。 */
export function objectHistoryFilter(query: Readonly<Record<string, unknown>> = {}): ObjectHistoryFilter {
  const filter: ObjectHistoryFilter = {};
  for (const key of ['from', 'to', 'actorId', 'eventType', 'objectType', 'ipAddress', 'description'] as const) {
    const value = query[key];
    if (value === undefined || value === null || value === '') continue;
    if (typeof value !== 'string') throw new StandardCrudRequestError('操作履历筛选无效。', 400);
    filter[key] = value;
  }
  return filter;
}

/** 存储侧已完成安全过滤与分页；不在前端裁剪、不把故障或无效页伪装成零条记录。 */
export async function readObjectHistoryPage(target: ObjectHistoryTarget, query: Readonly<Record<string, unknown>>,
  context: ObjectHistoryReadContext, fetcher: typeof fetch): Promise<ObjectHistoryPage> {
  if (!context.featurePosition?.trim())
    throw new StandardCrudRequestError('缺少操作履历功能位置。', 400, {}, { reasonKey: 'ObjectHistory.FeatureRequired' });
  const url = new URL(historyPath(target, objectHistoryFilter(query)), 'http://object-history.local');
  for (const key of ['pageIndex', 'pageSize'] as const) {
    const value = query[key];
    if (value === undefined) continue;
    if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 1)
      throw new StandardCrudRequestError('操作履历分页无效。', 400);
    url.searchParams.set(key, String(value));
  }
  const response = await fetcher(url.pathname + url.search, { method: 'GET', signal: context.signal,
    headers: headers(context) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  const value: unknown = await response.json();
  if (!isStandardCrudRecord(value) || !Array.isArray(value.items)
    || typeof value.total !== 'number' || !Number.isSafeInteger(value.total) || value.total < 0
    || typeof value.pageIndex !== 'number' || !Number.isSafeInteger(value.pageIndex) || value.pageIndex < 1
    || typeof value.pageSize !== 'number' || !Number.isSafeInteger(value.pageSize) || value.pageSize < 1) return invalidResponse();
  return { items: value.items.map(readObjectHistoryItem), total: value.total, pageIndex: value.pageIndex, pageSize: value.pageSize };
}

export type ObjectHistoryOutputMode = 'selected' | 'all';
export interface ObjectHistoryOutputQuery extends ObjectHistoryFilter { pageIndex: number; pageSize: number }

/** 输出只采用宿主已应用快照的安全键，不使用列表草稿、列投影或通用导出参数。 */
export function objectHistoryOutputQuery(query: Readonly<Record<string, unknown>> | undefined): ObjectHistoryOutputQuery {
  if (!query) throw new StandardCrudRequestError('缺少已应用的履历查询。', 400, {}, { reasonKey: 'ObjectHistory.QueryRequired' });
  const pageIndex = query.pageIndex ?? query.page ?? 1;
  const pageSize = query.pageSize ?? 20;
  if (typeof pageIndex !== 'number' || !Number.isSafeInteger(pageIndex) || pageIndex < 1
    || typeof pageSize !== 'number' || !Number.isSafeInteger(pageSize) || pageSize < 1)
    throw new StandardCrudRequestError('操作履历分页无效。', 400);
  return { pageIndex, pageSize, ...objectHistoryFilter(query) };
}

async function historyOutputResponse(target: ObjectHistoryTarget, output: 'export' | 'print', mode: ObjectHistoryOutputMode,
  selectedIds: readonly string[], filter: ObjectHistoryOutputQuery, context: ObjectHistoryReadContext, fetcher: typeof fetch): Promise<Response> {
  if (!context.featurePosition?.trim())
    throw new StandardCrudRequestError('缺少操作履历功能位置。', 400, {}, { reasonKey: 'ObjectHistory.FeatureRequired' });
  if (mode === 'selected' && (selectedIds.length === 0 || selectedIds.some(id => !id.trim())))
    throw new StandardCrudRequestError('请先在履历列表选择记录。', 400, {}, { reasonKey: 'ObjectHistory.SelectionRequired' });
  const response = await fetcher(historyPath(target, {}, output), { method: 'POST', signal: context.signal,
    headers: { ...headers(context), 'Content-Type': 'application/json' },
    body: JSON.stringify({ mode, ...(mode === 'selected' ? { selectedIds: [...selectedIds] } : {}), filter }) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return response;
}

/** owner 安全输出与普通 CRUD 导出协议独立；文件名仅作为下载建议，不参与路径定位。 */
export async function exportObjectHistory(target: ObjectHistoryTarget, mode: ObjectHistoryOutputMode, selectedIds: readonly string[],
  filter: ObjectHistoryOutputQuery, context: ObjectHistoryReadContext, fetcher: typeof fetch): Promise<{ blob: Blob; fileName: string }> {
  const response = await historyOutputResponse(target, 'export', mode, selectedIds, filter, context, fetcher);
  const disposition = response.headers.get('content-disposition');
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition ?? '')?.[1];
  const plain = /filename="?([^";]+)"?/i.exec(disposition ?? '')?.[1];
  let fileName = 'object-history.xlsx';
  try { fileName = (encoded ? decodeURIComponent(encoded) : plain) ?? fileName; } catch { /* 无效建议名不影响已授权的文件响应。 */ }
  fileName = Array.from(fileName.replace(/[\\/<>:"|?*]/g, '_'), character => character.charCodeAt(0) < 32 ? '_' : character).join('');
  return { blob: await response.blob(), fileName: fileName || 'object-history.xlsx' };
}

/** 打印和导出使用相同安全集合；响应重新复制白名单，不接受管理员 payload 作打印数据。 */
export async function printObjectHistory(target: ObjectHistoryTarget, mode: ObjectHistoryOutputMode, selectedIds: readonly string[],
  filter: ObjectHistoryOutputQuery, context: ObjectHistoryReadContext, fetcher: typeof fetch): Promise<ObjectHistoryItem[]> {
  const response = await historyOutputResponse(target, 'print', mode, selectedIds, filter, context, fetcher);
  const value: unknown = await response.json();
  if (!isStandardCrudRecord(value) || !Array.isArray(value.items)) return invalidResponse();
  return value.items.map(readObjectHistoryItem);
}
