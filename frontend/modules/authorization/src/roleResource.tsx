import { UserRoleInput } from './userRoleInput';
import { useEffect, useState } from 'react';
import { Alert, Checkbox, Spin, Typography } from 'antd';
import { platformFetch } from '@icp-platform/core';
import { createStandardCrudRestDataAdapter, isStandardCrudRecord, StandardCrudRequestError,
  type StandardCrudActionInputRenderContext, type StandardCrudResourceRegistration } from '@icp-platform/sdk';

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
  let response: Response;
  try { response = await fetcher(url, init); }
  catch (error) {
    if (init?.signal?.aborted) throw error;
    throw new Error(init?.method ? '操作结果未知，请刷新后核对；系统不会自动重试。' : '角色读取失败，请重试。');
  }
  if (!response.ok) {
    const problem: unknown = await response.json().catch(() => null);
    throw new StandardCrudRequestError(isStandardCrudRecord(problem) && typeof problem.title === 'string' ? problem.title : '角色操作失败', response.status);
  }
  return response.status === 204 ? undefined : response.json();
}
interface PermissionOption { code: string; displayName: string; moduleId: string; group?: string }
export function RolePermissionInput({ detail, row, value, onChange }: StandardCrudActionInputRenderContext) {
  const id = String(fields(detail ?? row).EID ?? '');
  const [options, setOptions] = useState<PermissionOption[] | null>(null);
  const [error, setError] = useState('');
  // 加载只依赖角色标识，用户编辑由共享动作弹层持有；卸载取消请求和过期回写。
  useEffect(() => {
    const controller = new AbortController();
    setOptions(null);
    setError('');
    onChange(undefined);
    void Promise.all([
      requestRole(platformFetch as typeof fetch, '/api/authorization/permissions', { signal: controller.signal }),
      requestRole(platformFetch as typeof fetch, `${route}/${encodeURIComponent(id)}/permissions`, { signal: controller.signal }),
    ]).then(([catalog, assigned]) => {
      if (controller.signal.aborted) return;
      if (!Array.isArray(catalog) || !isStandardCrudRecord(assigned) || !Array.isArray(assigned.permissionCodes)) throw new Error('权限目录响应无效');
      setOptions(catalog as PermissionOption[]);
      onChange({ codes: assigned.permissionCodes });
    }).catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : '权限读取失败'); });
    return () => controller.abort();
  // onChange 属于宿主当前动作实例，读取不会因受控输入改变而重复。
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);
  if (error) return <Alert type="error" message={error} />;
  if (!options) return <Spin />;
  const selected = isStandardCrudRecord(value) && Array.isArray(value.codes) ? value.codes as string[] : [];
  return <div><Typography.Paragraph>从当前生效目录选择角色权限。</Typography.Paragraph>
    <Checkbox.Group style={{ display: 'flex', flexDirection: 'column', gap: 8 }} value={selected}
      options={options.map(p => ({ value: p.code, label: `${p.moduleId} / ${p.group ?? ''} · ${p.displayName} (${p.code})` }))}
      onChange={codes => onChange({ codes })} />
  </div>;
}
export function createRoleResource(fetcher: typeof fetch = platformFetch as typeof fetch): StandardCrudResourceRegistration {
  const list = createStandardCrudRestDataAdapter<Record<string, unknown>>({ apiRoute: `${route}/query`, fetcher,
    mapListItem: value => { if (!isStandardCrudRecord(value) || typeof value.EID !== 'string') throw new Error('角色列表响应无效'); return { ...value, id: value.EID }; }, getRowId: row => String(row.id) });
  return {
    resourceKey: 'authorization.role', listViewKey: 'authorization.role.list', detailViewKey: 'authorization.role.detail',
    dataAdapter: {
      list: list.list, getRowId: list.getRowId,
      get: async (id, context) => { const normalized = { fields: mapRole(await requestRole(fetcher, `${route}/${encodeURIComponent(id)}`, { signal: context?.signal })), summaries: {} }; return { normalized, value: normalized }; },
      create: async values => ({ operation: 'create', payload: await requestRole(fetcher, route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ Code: values.ECODE, Name: values.ENAME, Description: values.Description }) }) }),
      update: async (id, values, current) => ({ operation: 'update', payload: await requestRole(fetcher, `${route}/${encodeURIComponent(id ?? '')}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ Name: values.ENAME, Description: values.Description, IsEnabled: current?.IsEnabled }) }) }),
    },
    actionStateResolver: ({ action, detail, row }) => {
      const current = fields(detail ?? row);
      const protectedAction = (current.ECODE === 'LocalAdmin' && ['edit', 'permissions'].includes(action.key)) || (current.IsBuiltIn === true && action.key === 'edit');
      return { visible: !protectedAction, available: !protectedAction };
    },
    actionHandlers: { userRoles: async ({ input }) => {
      if (!isStandardCrudRecord(input) || typeof input.accountId !== 'string' || !Array.isArray(input.codes)) throw new Error('请选择账号并等待角色读取成功。');
      await requestRole(fetcher, `/api/authorization/users/${encodeURIComponent(input.accountId)}/roles`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ RoleCodes: input.codes }) });
    }, permissions: async ({ id, input }) => {
      if (!id || !isStandardCrudRecord(input) || !Array.isArray(input.codes)) throw new Error('请等待权限目录读取成功。');
      await requestRole(fetcher, `${route}/${encodeURIComponent(id)}/permissions`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ PermissionCodes: input.codes }) });
    } },
    actionInputRenderers: { userRoles: context => <UserRoleInput {...context} />, permissions: context => <RolePermissionInput {...context} /> },
  };
}
export const roleResource = createRoleResource();
