/// <reference types="vite/client" />
// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ObjectAuthorizationInput } from '@icp-platform/ui';
import dictionaryText from '../../../../../modules/Authorization/metadata/dictionaries/DataScope.dic.json?raw';

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
const dictionary = JSON.parse(dictionaryText)
  .DataScope as Record<string, { zhs: string; en: string }>;
const catalogs = { zhs: Object.fromEntries(Object.entries(dictionary).map(([key, value]) => [`DataScope.${key}`, value.zhs])) };
async function mount(inspect: boolean) {
  const container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  function Harness() {
    const [value, onChange] = useState<unknown>();
    return <PlatformQueryProvider scopeKey="example-permissions"><ObjectAuthorizationInput row={row}
      target={{ resourceKey: 'example.record', title: '记录', id: row.id, name: row.ENAME }}
      action={{ key: inspect ? 'viewAuthorization' : 'authorize', labelKey: '授权', inputKey: inspect ? 'objectAuthorizationDetails' : 'objectAuthorization' }}
      featurePosition="record-authorize" value={value} onChange={onChange} onDraftStateChange={draft} /></PlatformQueryProvider>;
  }
  await act(async () => root.render(<LocalizationProvider initialLanguage="zhs" defaultLanguage="zhs" catalogs={catalogs}
    supportedLanguages={[{ code: 'zhs', displayName: '简体中文', locale: 'zh-CN' }]}><Harness /></LocalizationProvider>));
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
    : { ok: true, json: async () => path.endsWith('/catalog') ? catalog : { items: [], hasMore: false, currentValues: [] } });
  await mount(true);
  expect(document.body.textContent).toContain('操作失败，请稍后重试。');
  expect(document.querySelector('.ant-collapse')).toBeNull();
  expect(draft).toHaveBeenLastCalledWith({ ready: true, dirty: false });
});

it('resolves nonempty explanation subjects in bounded batches through the object-qualified directory', async () => {
  const subjects = Array.from({ length: 11 }, (_, index) => ({ id: `s-${index}`, type: 'department', subjectId: `d-${index}`, priority: index, actions: { read: 'allow' } }));
  const explanation = { accountId: 'Admin', accountName: '管理员', actions: [{ key: 'read', title: '读取', allowed: true, reason: 'rule', sources: [
    { kind: 'rule', id: 'r', name: '根规则', opinion: 'allow', status: 'effective', subjects: [...subjects, { ...subjects[0], id: 'duplicate' }] },
  ] }] };
  fetcher.mockImplementation(async (path: string) => ({ ok: true, json: async () => {
    if (path.endsWith('/catalog')) return { ...catalog, subjectTypes: [{ key: 'department', title: '部门', requiresId: true }] };
    if (path.includes('/subjects/user')) return { items: [], hasMore: false, currentValues: [] };
    if (path.includes('/subjects/department')) return { items: [], hasMore: false,
      currentValues: new URL(path, 'https://host.test').searchParams.getAll('currentIds').filter(id => id !== 'd-10')
        .map(id => ({ id, name: `部门 ${id}`, path: [{ type: 'company', id: 'c', name: '路径公司' }] })) };
    return explanation;
  } }));
  await mount(true);
  const reads = fetcher.mock.calls.filter(([path]) => path.includes('/subjects/department'));
  expect(reads).toHaveLength(2);
  expect(reads.map(([path]) => new URL(path, 'https://host.test').searchParams.getAll('currentIds')))
    .toEqual([subjects.slice(0, 10).map(subject => subject.subjectId), ['d-10']]);
  expect(reads.every(([path, options]) => path.startsWith('/api/authorization/data-scope/objects/example.record/Record_1/subjects/')
    && options.headers['X-NewP-Feature'] === 'record-authorize' && options.signal instanceof AbortSignal)).toBe(true);
  await act(async () => (document.querySelector('.ant-collapse-header') as HTMLElement).click());
  expect(document.body.textContent).toContain('部门 d-0');
  expect(document.body.textContent).toContain('d-10');
  expect(document.body.textContent).not.toContain('路径公司');
  expect(fetcher.mock.calls.every(([, options]) => !options.method)).toBe(true);
});

it('does not publish a partial explanation when its subject directory rejects the management qualification', async () => {
  fetcher.mockImplementation(async (path: string) => path.includes('/subjects/department') ? { ok: false, status: 403 } : { ok: true,
    json: async () => path.endsWith('/catalog') ? catalog : path.includes('/subjects/user') ? { items: [], hasMore: false, currentValues: [] }
      : { accountId: 'Admin', accountName: '管理员', actions: [{ key: 'read', title: '读取', allowed: true, reason: 'rule', sources: [
        { kind: 'rule', id: 'r', name: '不应显示的来源', opinion: 'allow', status: 'effective', subjects: [{ id: 's', type: 'department', subjectId: 'd', priority: 0, actions: {} }] },
      ] }] } });
  await mount(true);
  expect(document.body.textContent).toContain('当前没有对象授权资格');
  expect(document.querySelector('.ant-collapse')).toBeNull();
  expect(document.body.textContent).not.toContain('不应显示的来源');
  expect(fetcher.mock.calls.filter(([path]) => path.includes('/subjects/department'))).toHaveLength(1);
});
