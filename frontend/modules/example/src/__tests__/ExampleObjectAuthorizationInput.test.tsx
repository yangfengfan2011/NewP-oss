// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { PlatformQueryProvider } from '@icp-platform/core';
import { ObjectAuthorizationInput } from '@icp-platform/ui';

const fetcher = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: fetcher }));
(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);
let root: Root;
afterEach(async () => { await act(async () => root?.unmount()); document.body.innerHTML = ''; fetcher.mockReset(); });
const catalog = { actions: [{ key: 'read', title: '读取', enabled: true }, { key: 'create', title: '新建', enabled: true }],
  subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }] };
const row = { id: 'Record_1', ENAME: '测试记录' };
const draft = vi.fn();
async function mount(inspect: boolean) {
  const container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  function Harness() {
    const [value, onChange] = useState<unknown>();
    return <PlatformQueryProvider scopeKey="example-permissions"><ObjectAuthorizationInput row={row}
      target={{ resourceKey: 'example.record', title: '记录', id: row.id, name: row.ENAME }}
      action={{ key: inspect ? 'viewAuthorization' : 'authorize', labelKey: '授权', inputKey: inspect ? 'objectAuthorizationDetails' : 'objectAuthorization' }}
      featurePosition="record-authorize" value={value} onChange={onChange} onDraftStateChange={draft} /></PlatformQueryProvider>;
  }
  await act(async () => root.render(<Harness />));
}

it('loads saved subjects, keeps tri-state changes in the draft and reports nested editor readiness', async () => {
  fetcher.mockImplementation(async (path: string) => ({ ok: true, json: async () => path.endsWith('/catalog') ? catalog
    : { resourceKey: 'example.record', objectId: row.id, subjects: [{ id: 's', type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } }] } }));
  await mount(false);
  expect(document.body.textContent).toContain('目标记录：测试记录');
  expect(document.body.textContent).not.toContain('记录标识：');
  expect([...document.querySelectorAll('th')].map(cell => cell.textContent)).not.toContain('新建');
  expect(draft).toHaveBeenLastCalledWith({ ready: true, dirty: false });
  await act(async () => (document.querySelector('[role="checkbox"]') as HTMLElement).click());
  expect(draft).toHaveBeenLastCalledWith({ ready: true, dirty: true });
  const add = [...document.querySelectorAll('button')].find(button => button.textContent === '添加主体项');
  await act(async () => add!.click());
  expect(draft).toHaveBeenLastCalledWith({ ready: false, dirty: true });
  const drawer = document.querySelector('.ant-drawer')!;
  expect([...drawer.querySelectorAll('.ant-form-item-label')].map(label => label.textContent)).toContain('读取');
  expect(drawer.textContent).not.toContain('意见');
  expect(drawer.textContent).not.toContain('新建');
  expect(drawer.textContent).not.toContain('保存根规则');
  expect(drawer.textContent).toContain('保存后生效');
  expect(fetcher.mock.calls.every(([, options]) => !options.method)).toBe(true);
});

it('defaults to the current user, explains rule provenance and allows selecting another user without writes', async () => {
  fetcher.mockImplementation(async (path: string) => ({ ok: true, json: async () => path.endsWith('/catalog') ? catalog
    : path.includes('/subjects/user') ? { items: [{ id: 'User_2', name: '普通用户' }], hasMore: false, currentValues: [] }
      : { accountId: path.includes('accountId=') ? 'User_2' : 'Admin', accountName: path.includes('accountId=') ? '普通用户' : '管理员',
        actions: [{ key: 'read', title: '读取', allowed: !path.includes('accountId='), reason: path.includes('accountId=') ? 'rule' : 'administrator',
          sources: path.includes('accountId=') ? [{ kind: 'rule', id: 'r', name: '根规则 / 子规则', opinion: 'deny', status: 'effective', subjects: [] }] : [] }] } }));
  await mount(true);
  expect(fetcher.mock.calls.some(([path]) => path.endsWith('/explanation'))).toBe(true);
  expect(document.body.textContent).toContain('分析用户：管理员');
  expect(document.body.textContent).not.toContain('记录标识：');
  await act(async () => (document.querySelector('.ant-select-selector') as HTMLElement).dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  const option = [...document.querySelectorAll('.ant-select-item-option')].find(node => node.textContent === '普通用户');
  expect(option).toBeDefined();
  await act(async () => (option as HTMLElement).click());
  expect(fetcher.mock.calls.some(([path]) => path.endsWith('/explanation?accountId=User_2'))).toBe(true);
  await act(async () => (document.querySelector('.ant-collapse-header') as HTMLElement).click());
  expect(document.body.textContent).toContain('根规则 / 子规则');
  expect(document.body.textContent).toContain('参与最终裁决');
  expect(document.querySelector('[role="checkbox"]')).toBeNull();
  expect(draft).toHaveBeenLastCalledWith({ ready: true, dirty: false });
  expect(fetcher.mock.calls.every(([, options]) => !options.method)).toBe(true);
});

it('shows an analysis failure instead of a permission result', async () => {
  fetcher.mockImplementation(async (path: string) => path.endsWith('/explanation') ? { ok: false, status: 500 }
    : { ok: true, json: async () => path.endsWith('/catalog') ? catalog : { items: [], hasMore: false } });
  await mount(true);
  expect(document.body.textContent).toContain('对象权限读取失败');
  expect(document.querySelector('.ant-collapse')).toBeNull();
  expect(draft).toHaveBeenLastCalledWith({ ready: true, dirty: false });
});
