import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionInstance } from '@icp-platform/sdk';

export interface ObjectAuthorizationAddress { resourceKey: string; id?: string; basePath?: string }

class ObjectAuthorizationSubmission {
  private unknown = false;
  private readonly listeners = new Set<() => void>();
  readonly getSnapshot = () => this.unknown;
  readonly subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  freeze() { if (!this.unknown) { this.unknown = true; for (const listener of this.listeners) listener(); } }
  dispose() { this.listeners.clear(); }
}
const submissionOwner = Symbol('object-authorization-submission');

/** 未知结果属于页面/主体实例和完整来源；关弹窗不能把它变成可重复提交的新操作。 */
export function objectAuthorizationSubmission(instance: StandardCrudActionInstance | undefined, target: ObjectAuthorizationAddress, featurePosition?: string) {
  if (!instance?.active) return undefined;
  const entries = instance.getOrCreate(submissionOwner, () => new Map<string, ObjectAuthorizationSubmission>(), values => {
    for (const value of values.values()) value.dispose();
    values.clear();
  });
  const key = JSON.stringify([target.resourceKey, target.id, objectAuthorizationPath(target), featurePosition]);
  let state = entries.get(key);
  if (!state) { state = new ObjectAuthorizationSubmission(); entries.set(key, state); }
  return state;
}

export function objectAuthorizationOutcomeUnknown(failure: unknown): boolean {
  if (!failure) return false;
  return !(failure instanceof StandardCrudRequestError) || failure.status === 0 || failure.status >= 500
    || failure.code === 'Persistence.CommitOutcomeUnknown' || failure.code === 'Platform.MutationOutcomeUnknown'
    || failure.reasonKey === 'AuthorizationMessages.OperationUnknown';
}

/** owner 来源参数属于整个请求；子路径和分析/目录参数不能接在已有 query 后或丢失其范围。 */
export function objectAuthorizationPath(target: ObjectAuthorizationAddress, suffix = ''): string {
  const base = target.basePath ?? `/api/authorization/data-scope/objects/${encodeURIComponent(target.resourceKey)}/${encodeURIComponent(target.id ?? '')}`;
  const url = new URL(base, 'http://object-authorization.local');
  const child = new URL(suffix.replace(/^\//, ''), 'http://object-authorization.local/');
  if (suffix) url.pathname = `${url.pathname.replace(/\/$/, '')}/${child.pathname.replace(/^\//, '')}`;
  for (const [key, value] of child.searchParams) url.searchParams.append(key, value);
  return url.pathname + url.search;
}

/** 对象授权的传输边界保留错误事实；资格探测仍由调用者处理 403/404。 */
export async function fetchObjectAuthorization(fetcher: typeof fetch, url: string, init?: RequestInit): Promise<Response> {
  try { return await fetcher(url, init); }
  catch (error) {
    if (init?.signal?.aborted) throw error;
    const read = !init?.method || init.method === 'GET';
    throw new StandardCrudRequestError('对象授权请求失败。', 0, {},
      { reasonKey: read ? 'DataScope.ObjectReadFailed' : 'AuthorizationMessages.OperationUnknown' }, undefined, error);
  }
}

/** 保留服务端业务细节；无 Problem 的目标拒绝使用对象管理语义呈现。 */
export async function objectAuthorizationError(response: Response): Promise<StandardCrudRequestError> {
  const error = await readStandardCrudRequestError(response);
  if (error.reasonKey || error.code) return error;
  const reasonKey = response.status === 403 ? 'DataScope.ObjectForbidden'
    : response.status === 404 ? 'DataScope.ObjectNotFound' : undefined;
  return reasonKey ? new StandardCrudRequestError(error.message, error.status, error.fieldErrors,
    { reasonKey }, error.responseProblem, error) : error;
}
