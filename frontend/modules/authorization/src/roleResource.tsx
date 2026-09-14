import { UserRoleInput } from './userRoleInput';
import { platformFetch } from '@icp-platform/core';
import { createStandardCrudRestDataAdapter, isStandardCrudRecord, StandardCrudRequestError,
  type StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const featureHeaders = (position?: string): Record<string, string> => position ? { 'X-NewP-Feature': position } : {};
export const featureInit = (position?: string): RequestInit => position ? { headers: featureHeaders(position) } : {};

const route = '/api/authorization/roles';
function fields(value: unknown): Record<string, unknown> {
  const row = isStandardCrudRecord(value) ? value : {};
  return isStandardCrudRecord(row.fields) ? row.fields : row;
}
function mapRole(value: unknown) {
  if (!isStandardCrudRecord(value) || typeof value.id !== 'string') throw new Error('角色响应无效');
  return { EID: value.id, ECODE: value.code, ENAME: value.displayName, Description: value.description,
    IsEnabled: value.isEnabled, IsBuiltIn: value.isBuiltIn, CreatedBy: value.createdBy, CreatedAt: value.createdAt, UpdatedBy: value.updatedBy, UpdatedAt: value.updatedAt,
    CreatedBy_DisplayValue: value.createdBy_DisplayValue, CreatedAt_DisplayValue: value.createdAt_DisplayValue,
    UpdatedBy_DisplayValue: value.updatedBy_DisplayValue, UpdatedAt_DisplayValue: value.updatedAt_DisplayValue };
}
// 复用平台认证、防伪及请求错误；写入不重试，网络不确定时先核对实际结果。
export async function requestRole(fetcher: typeof fetch, url: string, init?: RequestInit): Promise<unknown> {
  try {
    const response = await fetcher(url, init);
    if (!response.ok) {
      const problem: unknown = await response.json().catch(() => null);
      throw new StandardCrudRequestError(isStandardCrudRecord(problem) && typeof problem.title === 'string' ? problem.title : '角色操作失败', response.status);
    }
    return response.status === 204 ? undefined : await response.json();
  } catch (error) {
    if (error instanceof StandardCrudRequestError || init?.signal?.aborted) throw error;
    const isRead = ['GET', 'HEAD', 'OPTIONS'].includes((init?.method ?? 'GET').toUpperCase());
    throw new Error(isRead ? '角色读取失败，请重试。' : '操作结果未知，请刷新后核对；系统不会自动重试。');
  }
}
export function createRoleResource(fetcher: typeof fetch = platformFetch as typeof fetch): StandardCrudResourceRegistration {
  const list = createStandardCrudRestDataAdapter<Record<string, unknown>>({ apiRoute: `${route}/query`, fetcher,
    mapListItem: value => { if (!isStandardCrudRecord(value) || typeof value.EID !== 'string') throw new Error('角色列表响应无效'); return { ...value, id: value.EID }; }, getRowId: row => String(row.id) });
  return {
    resourceKey: 'authorization.role', listViewKey: 'authorization.role.list', detailViewKey: 'authorization.role.detail',
    dataAdapter: {
      list: list.list, getRowId: list.getRowId,
      get: async (id, context) => { const normalized = { fields: mapRole(await requestRole(fetcher, `${route}/${encodeURIComponent(id)}`, { signal: context?.signal, ...featureInit(context?.featurePosition) })), summaries: {} }; return { normalized, value: normalized }; },
      create: async (values, _current, context) => ({ operation: 'create', payload: await requestRole(fetcher, route, { method: 'POST', headers: { 'Content-Type': 'application/json', ...featureHeaders(context?.featurePosition) }, body: JSON.stringify({ Code: values.ECODE, Name: values.ENAME, Description: values.Description }) }) }),
      update: async (id, values, current, context) => ({ operation: 'update', payload: await requestRole(fetcher, `${route}/${encodeURIComponent(id ?? '')}`, { method: 'PUT', headers: { 'Content-Type': 'application/json', ...featureHeaders(context?.featurePosition) }, body: JSON.stringify({ Name: values.ENAME, Description: values.Description, IsEnabled: current?.IsEnabled }) }) }),
    },
    actionStateResolver: ({ action, detail, row }) => {
      const current = fields(detail ?? row);
      const protectedAction = (current.ECODE === 'LocalAdmin' && action.key === 'edit') || (current.IsBuiltIn === true && action.key === 'edit');
      return { visible: !protectedAction, available: !protectedAction };
    },
    actionHandlers: { userRoles: async ({ input, featurePosition }) => {
      if (!isStandardCrudRecord(input) || typeof input.accountId !== 'string' || !Array.isArray(input.codes)) throw new Error('请选择账号并等待角色读取成功。');
      await requestRole(fetcher, `/api/authorization/users/${encodeURIComponent(input.accountId)}/roles`, { method: 'PUT', headers: { 'Content-Type': 'application/json', ...featureHeaders(featurePosition) }, body: JSON.stringify({ RoleCodes: input.codes }) });
    }, permissions: async ({ id }) => {
      if (!id) throw new Error('请选择一个角色。');
      return { operation: 'action', payload: null, navigation: { entryKey: 'authorization.workbench', parameters: { roleId: id } } };    } },
    actionInputRenderers: { userRoles: context => <UserRoleInput {...context} /> },
  };
}
export const roleResource = createRoleResource();
