// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider, invalidatePlatformAntiforgeryToken, platformFetch, usePlatformQuery } from '@icp-platform/core';
import { createStandardCrudClient } from '@icp-platform/sdk';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';

// 本测试要覆盖真实网络边界，撤销通用 AntD 夹具的 fetch 直通替身。
vi.unmock('@icp-platform/core');

const initialUser = { id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false, isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', permissions: ['Resource.View', 'Resource.Update'] };
const container = document.createElement('div');
let root: ReturnType<typeof createRoot> | undefined;
afterEach(async () => { if (root) await act(async () => root?.unmount()); root = undefined; container.replaceChildren(); invalidatePlatformAntiforgeryToken(); vi.unstubAllGlobals(); });

function Consumer() {
  const auth = useAuth();
  const navigate = useNavigate();
  const query = usePlatformQuery({ key: ['resource'], load: async () => (await platformFetch('/api/resource')).json() as Promise<{ value: string }> });
  return <><span>{query.data?.value}</span>{auth.hasPermission('Resource.Update') && <button>编辑</button>}<button onClick={() => navigate('/other')}>进入另一页面</button></>;
}
async function render(user = initialUser) {
  await act(async () => { root = createRoot(container); root.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: {} }}><AuthProvider initialUser={user}><CurrentPermissionScope><Consumer /></CurrentPermissionScope></AuthProvider></LocalizationProvider></MemoryRouter>); });
}
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });

it('refreshes buttons and drops cached data after an explicit rejected write without replay', async () => {
  let reads = 0;
  const fetcher = vi.fn(async (input: RequestInfo | URL) => {
    const url = input instanceof Request ? input.url : String(input);
    if (url.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.endsWith('current-user')) return json({ ...initialUser, permissions: ['Resource.View'] });
    if (input instanceof Request && input.method === 'PUT') return new Response('{"code":"AUTH_FORBIDDEN"}', { status: 403 });
    return json({ value: ++reads === 1 ? '旧结果' : '当前结果' });
  });
  vi.stubGlobal('fetch', fetcher);
  await render();
  expect(container.textContent).toContain('旧结果');
  expect(container.textContent).toContain('编辑');
  await act(async () => { await expect(createStandardCrudClient('/api/resource', platformFetch).update('1', {})).rejects.toThrow('当前操作已被拒绝'); });
  expect(container.textContent).not.toContain('编辑');
  expect(container.textContent).toContain('当前结果');
  expect(fetcher.mock.calls.filter(([request]) => request instanceof Request && request.method === 'PUT')).toHaveLength(1);
});

it.each([false, true])('re-enters using the latest grants (can edit: %s) with one authentication owner', async (canEdit) => {
  const fetcher = vi.fn(async (input: RequestInfo | URL) => String(input).endsWith('current-user')
    ? json({ ...initialUser, permissions: canEdit ? initialUser.permissions : ['Resource.View'] }) : json({ value: '数据' }));
  vi.stubGlobal('fetch', fetcher);
  await render({ ...initialUser, permissions: canEdit ? ['Resource.View'] : initialUser.permissions });
  await act(async () => { [...container.querySelectorAll('button')].find(button => button.textContent === '进入另一页面')!.click(); });
  expect(container.textContent?.includes('编辑')).toBe(canEdit);
  expect(fetcher.mock.calls.filter(([input]) => String(input).endsWith('current-user'))).toHaveLength(1);
});
