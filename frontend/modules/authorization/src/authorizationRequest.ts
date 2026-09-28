import { readStandardCrudRequestError, StandardCrudRequestError } from '@icp-platform/sdk';

// Authorization 各管理入口共用传输边界；保留事实，不在请求时固定用户语言。
export async function requestAuthorization(fetcher: typeof fetch, url: string, init?: RequestInit, subject = '角色'): Promise<unknown> {
  try {
    const response = await fetcher(url, init);
    if (!response.ok) throw await readStandardCrudRequestError(response);
    return response.status === 204 ? undefined : await response.json();
  } catch (error) {
    if (error instanceof StandardCrudRequestError || init?.signal?.aborted) throw error;
    const isRead = ['GET', 'HEAD', 'OPTIONS'].includes((init?.method ?? 'GET').toUpperCase());
    throw new StandardCrudRequestError(isRead ? `${subject}读取失败，请重试。` : '操作结果未知，请刷新后核对；系统不会自动重试。', 0, {},
      { reasonKey: isRead ? 'AuthorizationMessages.ReadFailed' : 'AuthorizationMessages.OperationUnknown' }, undefined, error);
  }
}
