import { requestAuthorization as requestRole } from './authorizationRequest';
export { requestAuthorization as requestRole } from './authorizationRequest';
import { UserRoleInput } from './userRoleInput';
import { platformFetch } from '@icp-platform/core';
import { createStandardCrudRestDataAdapter, isStandardCrudRecord, StandardCrudRequestError,
  standardCrudRequestHeaders, type ActionDataAccessRequirement, type StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const featureHeaders = (position?: string, dataAccess?: ActionDataAccessRequirement): Record<string, string> =>
  standardCrudRequestHeaders({ featurePosition: position, dataAccess });
export const featureInit = (position?: string, dataAccess?: ActionDataAccessRequirement): RequestInit =>
  position || dataAccess ? { headers: featureHeaders(position, dataAccess) } : {};

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
export function createRoleResource(fetcher: typeof fetch = platformFetch as typeof fetch): StandardCrudResourceRegistration {
  const list = createStandardCrudRestDataAdapter<Record<string, unknown>>({ apiRoute: `${route}/query`, fetcher,
    mapListItem: value => { if (!isStandardCrudRecord(value) || typeof value.EID !== 'string') throw new Error('角色列表响应无效'); return { ...value, id: value.EID }; }, getRowId: row => String(row.id) });
  return {
    resourceKey: 'authorization.role', listViewKey: 'authorization.role.list', detailViewKey: 'authorization.role.detail',
    dataAdapter: {
      list: list.list, getRowId: list.getRowId,
      get: async (id, context) => { const normalized = { fields: mapRole(await requestRole(fetcher, `${route}/${encodeURIComponent(id)}`, { signal: context?.signal, ...featureInit(context?.featurePosition, context?.dataAccess) })), summaries: {} }; return { normalized, value: normalized }; },
      create: async (values, _current, context) => ({ operation: 'create', payload: await requestRole(fetcher, route, { method: 'POST', headers: { 'Content-Type': 'application/json', ...standardCrudRequestHeaders(context) }, body: JSON.stringify({ Code: values.ECODE, Name: values.ENAME, Description: values.Description }) }) }),
      update: async (id, values, current, context) => ({ operation: 'update', payload: await requestRole(fetcher, `${route}/${encodeURIComponent(id ?? '')}`, { method: 'PUT', headers: { 'Content-Type': 'application/json', ...standardCrudRequestHeaders(context) }, body: JSON.stringify({ Name: values.ENAME, Description: values.Description, IsEnabled: current?.IsEnabled }) }) }),
    },
    actionStateResolver: ({ action, detail, row }) => {
      const current = fields(detail ?? row);
      const protectedAction = (current.ECODE === 'LocalAdmin' && action.key === 'edit') || (current.IsBuiltIn === true && action.key === 'edit');
      return { visible: !protectedAction, available: !protectedAction };
    },
    actionHandlers: { userRoles: async (context) => {
      const { input } = context;
      if (!isStandardCrudRecord(input) || typeof input.accountId !== 'string' || !Array.isArray(input.codes)) throw new StandardCrudRequestError('请选择账号并等待角色读取成功。', 0, {}, { reasonKey: 'AuthorizationMessages.AccountRequired' });
      await requestRole(fetcher, `/api/authorization/users/${encodeURIComponent(input.accountId)}/roles`, { method: 'PUT', headers: { 'Content-Type': 'application/json', ...standardCrudRequestHeaders(context) }, body: JSON.stringify({ RoleCodes: input.codes }) });
    }, permissions: async ({ id }) => {
      if (!id) throw new StandardCrudRequestError('请选择一个角色。', 0, {}, { reasonKey: 'AuthorizationMessages.RoleRequired' });
      return { operation: 'action', payload: null, navigation: { entryKey: 'authorization.workbench', parameters: { roleId: id } } };    } },
    actionInputRenderers: { userRoles: context => <UserRoleInput {...context} /> },
  };
}
export const roleResource = createRoleResource();
