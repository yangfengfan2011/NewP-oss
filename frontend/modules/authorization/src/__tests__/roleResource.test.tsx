import { describe, expect, it, vi } from 'vitest';
import { createRoleResource } from '../roleResource';
describe('普通角色正式适配器', () => {
  it('创建仅发送允许的字段，编辑保留停用状态且不回写保留身份', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(new Response('{}'));
    const resource = createRoleResource(fetcher);
    await resource.dataAdapter.create!({ ECODE: 'ordinary', ENAME: '角色', Description: '说明', IsBuiltIn: true });
    expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ Code: 'ordinary', Name: '角色', Description: '说明' });
    fetcher.mockResolvedValue(new Response('{}'));
    await resource.dataAdapter.update!('role-1', { ENAME: '改名', Description: '新说明', IsBuiltIn: true }, { IsEnabled: false });
    expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body))).toEqual({ Name: '改名', Description: '新说明', IsEnabled: false });
  });
  it('整组权限从动作输入发送，缺少有效读取结果时拒绝写入', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(new Response(null, { status: 204 }));
    const resource = createRoleResource(fetcher);
    await expect(resource.actionHandlers!.permissions({ id: 'role-1' })).rejects.toThrow();
    expect(fetcher).not.toHaveBeenCalled();
    await resource.actionHandlers!.permissions({ id: 'role-1', input: { codes: ['Organization.Person.View'] } });
    expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ PermissionCodes: ['Organization.Person.View'] });
  });
  it('不重试未知结果，403与Problem Details保持失败', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockRejectedValue(new TypeError('network'));
    const resource = createRoleResource(fetcher);
    await expect(resource.actionHandlers!.permissions({ id: 'role-1', input: { codes: [] } })).rejects.toThrow('不会自动重试');
    expect(fetcher).toHaveBeenCalledTimes(1);
    fetcher.mockResolvedValue(new Response(JSON.stringify({ title: '保护拒绝' }), { status: 403 }));
    await expect(resource.actionHandlers!.permissions({ id: 'role-1', input: { codes: [] } })).rejects.toMatchObject({ status: 403 });
  });
  it('普通停用角色仍可配置，管理员角色仅隐藏受保护写入动作', () => {
    const resource = createRoleResource();
    expect(resource.actionStateResolver!({ action: { key: 'permissions', labelKey: '' }, row: { ECODE: 'ordinary', IsEnabled: false } }).available).toBe(true);
    expect(resource.actionStateResolver!({ action: { key: 'permissions', labelKey: '' }, row: { ECODE: 'LocalAdmin' } }).visible).toBe(false);
  });
});
