import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import model from '../../../../../tests/Kernel.Tests/Fixtures/AuthorizationRoleRuntime.contract.json';
import list from '../../../../modules/platform/authorization/metadata/views/RoleList.view.json';
import detail from '../../../../modules/platform/authorization/metadata/views/RoleDetail.view.json';
import dictionary from '../../../../modules/platform/authorization/metadata/dictionaries/RoleView.dic.json';
import { createRoleResource } from '../../../../modules/platform/authorization/src/roleResource';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { EntryNavigationContext } from '../navigation/entryNavigation';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => { await Promise.all([import('@ant-design/pro-table'), import('@ant-design/pro-descriptions'), import('../pages/StandardCrudListDrawerRuntime')]); });
const entryPosition = 'Authorization/navigation:authorization.roles';
const workbenchPosition = 'Authorization/navigation:authorization.workbench';
const points = [
  { key: 'authorization.roles', kind: 'navigation', position: entryPosition },
  { key: 'authorization.workbench', kind: 'navigation', position: workbenchPosition },
  ...[['create', 'list.pageActions'], ['edit', 'list.rowActions'], ['userRoles', 'list.rowActions'], ['edit', 'detail.pageActions'], ['userRoles', 'detail.pageActions']]
    .map(([key, surface]) => ({ key, kind: 'action', position: `${entryPosition}/surface:${surface}/action:${key}`, parentPosition: entryPosition, actionScope: surface })),
];
function LanguageDriver() { const { currentLanguage, setLanguage } = useLocalization(); return <button data-language-driver onClick={() => setLanguage(currentLanguage === 'en' ? 'zhs' : 'en')}>switch</button>; }
const memberPosition = `${entryPosition}/surface:detail.pageActions/action:userRoles`;
it('角色列表、Drawer表单与独立详情消费正式runtime字段和动作', async () => {
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
  const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockImplementation(async (url, init) => {
    if (init?.method === 'PUT') return new Response(null, { status: 204 });
    if (String(url).includes('/users/candidates')) return new Response(JSON.stringify([{ id: 'user-1', code: 'account', name: '已有账号', status: 'Enabled' }]));
    if (String(url).endsWith('/users/user-1/roles')) return new Response(JSON.stringify({ roleCodes: ['offpage'] }));
    if (String(url).includes('pageIndex=2')) return new Response(JSON.stringify({ items: [{ EID: 'role-2', ECODE: 'offpage', ENAME: '另一页已配角色', IsEnabled: true }], total: 21 }));
    if (String(url).includes('/query')) return new Response(JSON.stringify({ items: [{ EID: 'role-1', ECODE: 'ordinary', ENAME: '真实角色', IsEnabled: false, IsBuiltIn: false }], total: 21 }));
    return new Response(JSON.stringify({ id: 'role-1', code: 'ordinary', displayName: '真实角色', isEnabled: false, isBuiltIn: false, createdBy: 'actor-raw-id', updatedBy: 'actor-raw-id', createdBy_DisplayValue: '当前管理员', updatedBy_DisplayValue: '当前管理员', createdAt_DisplayValue: '2026/9/10 09:00:00', updatedAt_DisplayValue: '2026/9/10 09:00:00', createdAt: '2026-09-10T01:00:00Z', updatedAt: '2026-09-10T01:00:00Z' }));
  });
  vi.stubGlobal('fetch', fetcher);
  const resource = createRoleResource(fetcher);
  const openEntry = vi.fn();
  const catalog = Object.fromEntries(Object.entries(dictionary.RoleView).map(([key, value]) => [`RoleView.${key}`, value.zhs]));
  const render = async (isDetail: boolean, canOpenWorkbench = true) => act(async () => root.render(<MemoryRouter>
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={{ zhs: catalog, en: Object.fromEntries(Object.entries(dictionary.RoleView).map(([key, value]) => ['RoleView.' + key, value.en])) }}>
      <LanguageDriver /><PlatformQueryProvider scopeKey="role-host"><AuthorizationProvider permissions={[]} featurePositions={points.filter(point => canOpenWorkbench || point.position !== workbenchPosition).map(point => point.position)}>
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Authorization', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: points }] }, modelDocuments: [model], viewDocuments: [list, detail], navDocuments: [{ type: 'nav', navigation: { items: [{ key: 'authorization.workbench', featurePosition: workbenchPosition }] } }] }}>
          <FeaturePlacementContext.Provider value={{ entryPosition }}><EntryNavigationContext.Provider value={openEntry}>{isDetail ? <StandardCrudDetailPageHost viewKey="authorization.role.detail" id="role-1" registration={resource} /> : <StandardCrudListPageHost viewKey="authorization.role.list" registration={resource} />}</EntryNavigationContext.Provider></FeaturePlacementContext.Provider>
        </RuntimeMetadataProvider>
      </AuthorizationProvider></PlatformQueryProvider>
    </LocalizationProvider>
  </MemoryRouter>));
  await render(false);
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('真实角色'); });
  await render(true);
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('账号角色'); });
  expect(container.textContent).toContain('创建人');
  expect(container.textContent).toContain('创建时间');
  expect(container.textContent).toContain('更新人');
  expect(container.textContent).toContain('更新时间');
  expect(container.textContent).toContain('当前管理员');
  expect(container.textContent).toContain('2026/9/10 09:00:00');
  expect(container.textContent).not.toContain('Field_');
  expect(container.textContent).not.toContain('actor-raw-id');
  expect(container.textContent).not.toContain('2026-09-10T01:00:00Z');
  const accounts = Array.from(container.querySelectorAll('button, a')).find(b => b.textContent === '账号角色') as HTMLElement | undefined;
  expect(accounts).toBeDefined();
  await act(async () => accounts!.click());
  await vi.waitFor(() => expect(document.body.querySelector('input[aria-label="账号"]')).not.toBeNull());
  await act(async () => document.body.querySelector('input[aria-label="账号"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.body.textContent).toContain('已有账号 (account)'));
  await act(async () => (document.body.querySelector('.ant-select-item-option') as HTMLElement).click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(document.body.textContent).toContain('真实角色 (ordinary)'); });
  expect(document.body.textContent).toContain('停用（不生效）');
  await act(async () => (document.body.querySelector('.ant-modal input[type="checkbox"]') as HTMLInputElement).click());
  await act(async () => (document.body.querySelector('.ant-modal .ant-pagination-next button') as HTMLButtonElement).click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(document.body.textContent).toContain('另一页已配角色'); });
  expect((document.body.querySelector('.ant-modal input[type="checkbox"]') as HTMLInputElement).checked).toBe(true);
  await act(async () => (document.body.querySelector('.ant-modal .ant-pagination-prev button') as HTMLButtonElement).click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(document.body.textContent).toContain('真实角色 (ordinary)'); });
  expect((document.body.querySelector('.ant-modal input[type="checkbox"]') as HTMLInputElement).checked).toBe(true);
  await act(async () => container.querySelector<HTMLButtonElement>('[data-language-driver]')!.click());
  expect(document.body.textContent).toContain('Configured roles: 2');
  expect(document.body.querySelector('.ant-modal input[aria-label="Account"]')).not.toBeNull();
  expect((document.body.querySelector('.ant-modal input[type="checkbox"]') as HTMLInputElement).checked).toBe(true);
  expect(document.body.querySelector('.ant-modal [aria-label="Language"]')).toBeNull();
  await act(async () => container.querySelector<HTMLButtonElement>('[data-language-driver]')!.click());
  const saveRoles = Array.from(document.body.querySelectorAll('.ant-modal button')).find(b => b.textContent?.replace(/\s/g, '') === '确认');
  await act(async () => (saveRoles as HTMLButtonElement).click());
  await vi.waitFor(() => expect(fetcher.mock.calls.some(([url, init]) => String(url).endsWith('/users/user-1/roles') && init?.method === 'PUT')).toBe(true));
  expect(JSON.parse(String(fetcher.mock.calls.find(([url, init]) => String(url).endsWith('/users/user-1/roles') && init?.method === 'PUT')![1]?.body))).toEqual({ RoleCodes: ['offpage', 'ordinary'] });
  for (const [url, init] of fetcher.mock.calls.filter(([url]) => String(url).includes('/users/'))) {
    expect(new Headers(init?.headers).get('X-NewP-Feature'), String(url)).toBe(memberPosition);
  }
  const roleOptions = fetcher.mock.calls.filter(([url, init]) => String(url).includes('/roles/query') && new Headers(init?.headers).get('X-NewP-Feature') === memberPosition);
  expect(roleOptions.length).toBeGreaterThanOrEqual(3);
  const roleInputRead = formalDataAccessFor(list.view.key, 'read');
  for (const [, init] of roleOptions) expect(new Headers(init?.headers).get('X-NewP-Data-Declaration')).toBe(roleInputRead.id);
  const roleDetails = fetcher.mock.calls.filter(([url, init]) => String(url).endsWith('/roles/role-1') && !init?.method);
  expect(roleDetails.length).toBeGreaterThan(0);
  for (const [, init] of roleDetails) expect(new Headers(init?.headers).get('X-NewP-Data-Declaration')).toBe(formalDataAccessFor(detail.view.key, 'read').id);
  await render(false);
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('真实角色'); });
  const create = Array.from(container.querySelectorAll('button')).find(b => b.textContent === '新建角色');
  expect(create).toBeDefined();
  await act(async () => create!.click());
  await vi.waitFor(async () => { await act(async () => undefined); expect(document.body.querySelector('.ant-drawer')).not.toBeNull(); });
  expect(document.body.textContent).toContain('角色代码');
  expect(document.body.textContent).toContain('角色名称');
  const nameInput = [...document.querySelectorAll<HTMLInputElement>('.ant-drawer input')].find(input => input.id.endsWith('ENAME'))!;
  expect(nameInput).toBeDefined();
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(nameInput, '角色草稿'); nameInput.dispatchEvent(new Event('input', { bubbles: true })); });
  await act(async () => container.querySelector<HTMLButtonElement>('[data-language-driver]')!.click());
  expect(document.body.textContent).toContain('Role code');
  expect(document.body.textContent).toContain('Role name');
  expect(nameInput.value).toBe('角色草稿');
  await act(async () => container.querySelector<HTMLButtonElement>('[data-language-driver]')!.click());
  expect(document.body.textContent).not.toContain('Field_');
  await render(true);
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('真实角色'); });
  expect(container.textContent).toContain('配置权限');
  expect(container.textContent).not.toContain('停用角色');
  const configure = Array.from(container.querySelectorAll('button')).find(b => b.textContent === '配置权限');
  await act(async () => configure!.click());
  expect(openEntry).toHaveBeenCalledWith({ entryKey: 'authorization.workbench', parameters: { roleId: 'role-1' } });
  await render(true, false);
  expect(container.textContent).not.toContain('配置权限');
  expect(container.textContent).toContain('账号角色');
});
