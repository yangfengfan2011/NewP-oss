import { readStandardCrudRequestError, StandardCrudRequestError } from '@icp-platform/sdk';

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
