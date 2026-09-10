import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import model from '../../../../../tests/Kernel.Tests/Fixtures/AuthorizationRoleRuntime.contract.json';
import list from '../../../../../modules/Authorization/metadata/views/RoleList.view.json';
import detail from '../../../../../modules/Authorization/metadata/views/RoleDetail.view.json';
import dictionary from '../../../../../modules/Authorization/metadata/dictionaries/RoleView.dic.json';
import { createRoleResource } from '../../../../modules/authorization/src/roleResource';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => { await Promise.all([import('@ant-design/pro-table'), import('@ant-design/pro-descriptions'), import('../pages/StandardCrudListDrawerRuntime')]); });
const permissions = ['Authorization.Role.View', 'Authorization.Role.Create', 'Authorization.Role.Edit', 'Authorization.Permission.Assign'];
it('角色列表、Drawer表单与独立详情消费正式runtime字段和动作', async () => {
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
  const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockImplementation(async (url, init) => {
    if (init?.method === 'PUT') return new Response(null, { status: 204 });
    if (String(url).endsWith('/role-1/permissions')) return new Response(JSON.stringify({ permissionCodes: [] }));
    if (String(url).endsWith('/authorization/permissions')) return new Response(JSON.stringify([{ code: 'Organization.Person.View', displayName: '查看人员', moduleId: 'Organization' }]));
    if (String(url).includes('/query')) return new Response(JSON.stringify({ items: [{ EID: 'role-1', ECODE: 'ordinary', ENAME: '真实角色', IsEnabled: false, IsBuiltIn: false }], total: 1 }));
    return new Response(JSON.stringify({ id: 'role-1', code: 'ordinary', displayName: '真实角色', isEnabled: false, isBuiltIn: false, createdBy: 'admin', updatedBy: 'admin', createdAt: '2026-09-10T01:00:00Z', updatedAt: '2026-09-10T01:00:00Z' }));
  });
  vi.stubGlobal('fetch', fetcher);
  const resource = createRoleResource(fetcher);
  const catalog = Object.fromEntries(Object.entries(dictionary.RoleView).map(([key, value]) => [`RoleView.${key}`, value.zhs]));
  const render = async (isDetail: boolean) => act(async () => root.render(<MemoryRouter>
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: catalog }}>
      <PlatformQueryProvider scopeKey="role-host"><AuthorizationProvider permissions={permissions}>
        <RuntimeMetadataProvider value={{ manifest: { modules: [] }, modelDocuments: [model], viewDocuments: [list, detail], navDocuments: [] }}>
          {isDetail ? <StandardCrudDetailPageHost viewKey="authorization.role.detail" id="role-1" registration={resource} /> : <StandardCrudListPageHost viewKey="authorization.role.list" registration={resource} />}
        </RuntimeMetadataProvider>
      </AuthorizationProvider></PlatformQueryProvider>
    </LocalizationProvider>
  </MemoryRouter>));
  await render(false);
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('真实角色'); });
  const create = Array.from(container.querySelectorAll('button')).find(b => b.textContent === '新建角色');
  expect(create).toBeDefined();
  await act(async () => create!.click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(document.body.querySelector('.ant-drawer')).not.toBeNull(); });
  expect(document.body.textContent).toContain('角色代码');
  expect(document.body.textContent).toContain('角色名称');
  expect(document.body.textContent).not.toContain('Field_');
  await render(true);
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('真实角色'); });
  expect(container.textContent).toContain('配置权限');
  expect(container.textContent).not.toContain('停用角色');
  const configure = Array.from(container.querySelectorAll('button')).find(b => b.textContent === '配置权限');
  await act(async () => configure!.click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(document.body.textContent).toContain('Organization.Person.View'); });
  await act(async () => (document.body.querySelector('.ant-modal input[type="checkbox"]') as HTMLInputElement).click());
  const confirm = Array.from(document.body.querySelectorAll('.ant-modal button')).find(b => b.textContent?.replace(/\s/g, '') === '确认');
  await act(async () => (confirm as HTMLButtonElement).click());
  await vi.waitFor(() => expect(fetcher.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true));
  const body = fetcher.mock.calls.find(([, init]) => init?.method === 'PUT')![1]?.body;
  expect(JSON.parse(String(body))).toEqual({ PermissionCodes: ['Organization.Person.View'] });
});
