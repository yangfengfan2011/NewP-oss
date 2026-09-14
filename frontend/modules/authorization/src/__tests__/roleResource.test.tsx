import { describe, expect, it, vi } from 'vitest';
import { createRoleResource, requestRole } from '../roleResource';
describe('普通角色正式适配器', () => {
  it('创建仅发送允许的字段，编辑保留停用状态且不回写保留身份', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(new Response('{}'));
    const resource = createRoleResource(fetcher);
    await resource.dataAdapter.create!({ ECODE: 'ordinary', ENAME: '角色', Description: '说明', IsBuiltIn: true }, undefined, { featurePosition: 'create-position' });
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-NewP-Feature')).toBe('create-position');
    expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ Code: 'ordinary', Name: '角色', Description: '说明' });
    fetcher.mockResolvedValue(new Response('{}'));
    await resource.dataAdapter.update!('role-1', { ENAME: '改名', Description: '新说明', IsBuiltIn: true }, { IsEnabled: false }, { featurePosition: 'edit-position' });
    expect(new Headers(fetcher.mock.calls[1][1]?.headers).get('X-NewP-Feature')).toBe('edit-position');
    expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body))).toEqual({ Name: '改名', Description: '新说明', IsEnabled: false });
  });
  it('列表和详情授权动作只返回工作台预选导航，不再读写旧权限弹窗', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>();
    const resource = createRoleResource(fetcher);
    await expect(resource.actionHandlers!.permissions({ id: 'role-1' })).resolves.toMatchObject({ navigation: { entryKey: 'authorization.workbench', parameters: { roleId: 'role-1' } } });
    expect(resource.actionInputRenderers?.permissions).toBeUndefined();
    expect(fetcher).not.toHaveBeenCalled();
  });
  it('不重试未知结果，403与Problem Details保持失败', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockRejectedValue(new TypeError('network'));
    await expect(requestRole(fetcher, '/api/authorization/workbench/roles/role-1', { method: 'PUT' })).rejects.toThrow('不会自动重试');
    expect(fetcher).toHaveBeenCalledTimes(1);
    fetcher.mockResolvedValue(new Response(JSON.stringify({ title: '保护拒绝' }), { status: 403 }));
    await expect(requestRole(fetcher, '/api/authorization/workbench/roles/role-1', { method: 'PUT' })).rejects.toMatchObject({ status: 403 });
  });
  it('完整保存账号集合保留页外选择，未知提交不自动重放', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(new Response(null, { status: 204 }));
    const resource = createRoleResource(fetcher);
    await expect(resource.actionHandlers!.userRoles({ input: undefined })).rejects.toThrow('请选择账号');
    expect(fetcher).not.toHaveBeenCalled();
    await resource.actionHandlers!.userRoles({ input: { accountId: 'user/1', codes: ['shown', 'offpage'] }, featurePosition: 'membership-position' });
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-NewP-Feature')).toBe('membership-position');
    expect(fetcher.mock.calls[0][0]).toBe('/api/authorization/users/user%2F1/roles');
    expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ RoleCodes: ['shown', 'offpage'] });
    fetcher.mockRejectedValue(new TypeError('network'));
    await expect(resource.actionHandlers!.userRoles({ input: { accountId: 'user/1', codes: [] } })).rejects.toThrow('不会自动重试');
    expect(fetcher).toHaveBeenCalledTimes(2);
  });
  it.each(['create', 'update'] as const)('成功 %s 响应体断流提示先核对实际结果且不重放', async operation => {
    const response = new Response('{}', { status: operation === 'create' ? 201 : 200 });
    vi.spyOn(response, 'json').mockRejectedValue(new TypeError('body stream terminated'));
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(response);
    const resource = createRoleResource(fetcher);
    const result = operation === 'create'
      ? resource.dataAdapter.create!({ ECODE: 'ordinary', ENAME: '角色' })
      : resource.dataAdapter.update!('role-1', { ENAME: '改名' }, { IsEnabled: true });
    await expect(result).rejects.toThrow('操作结果未知，请刷新后核对；系统不会自动重试。');
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
  it.each([undefined, 'GET'])('成功读取响应体断流保持普通读取错误（%s）', async method => {
    const response = new Response('{}');
    vi.spyOn(response, 'json').mockRejectedValue(new TypeError('body stream terminated'));
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(response);
    await expect(requestRole(fetcher, '/api/authorization/roles/role-1', { method })).rejects.toThrow('角色读取失败，请重试。');
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
  it('取消读取保留原始错误，204 不消费响应体', async () => {
    const controller = new AbortController();
    const failure = new DOMException('cancelled', 'AbortError');
    const response = new Response('{}');
    vi.spyOn(response, 'json').mockImplementation(async () => { controller.abort(); throw failure; });
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(response);
    await expect(requestRole(fetcher, '/api/authorization/roles/role-1', { signal: controller.signal })).rejects.toBe(failure);
    const empty = new Response(null, { status: 204 });
    const parse = vi.spyOn(empty, 'json');
    fetcher.mockResolvedValue(empty);
    await expect(requestRole(fetcher, '/api/authorization/roles/role-1/permissions', { method: 'PUT' })).resolves.toBeUndefined();
    expect(parse).not.toHaveBeenCalled();
    expect(fetcher).toHaveBeenCalledTimes(2);
  });
  it('普通停用角色仍可配置，管理员角色仅隐藏受保护写入动作', () => {
    const resource = createRoleResource();
    expect(resource.actionStateResolver!({ action: { key: 'permissions', labelKey: '' }, row: { ECODE: 'ordinary', IsEnabled: false } }).available).toBe(true);
    expect(resource.actionStateResolver!({ action: { key: 'permissions', labelKey: '' }, row: { ECODE: 'LocalAdmin' } }).visible).toBe(true);
  });
});
