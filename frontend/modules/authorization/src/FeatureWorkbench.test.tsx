// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import type { PageProps } from '@icp-platform/sdk';
import FeatureWorkbench from './FeatureWorkbench';
import { LocalizationProvider, useLocalization } from '@icp-platform/core';
import messageDictionary from '../../../../modules/Authorization/metadata/dictionaries/AuthorizationMessages.dic.json?raw';
import roleDictionary from '../../../../modules/Authorization/metadata/dictionaries/RoleView.dic.json?raw';

const fetcher = vi.hoisted(() => vi.fn());
const access = vi.hoisted(() => ({ positions: ['read', 'save'] }));
// 使用浏览器构建同源的实装 ESM，避免 CJS 与 Provider 分别持有一套 Context。
vi.mock('antd', async () => await import('antd/es'));
vi.mock('@icp-platform/core', async original => ({ ...await original<object>(), platformFetch: fetcher, useAuthorization: () => ({ hasFeature: (position: string) => access.positions.includes(position) }) }));
(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);
afterEach(() => { vi.clearAllMocks(); access.positions = ['read', 'save']; });
const json = (value: unknown) => new Response(JSON.stringify(value));
const role = (id: string) => ({ id, code: id, displayName: id, isEnabled: true });
const points = [{ position: 'read', title: '读取', behaviorKey: 'authorization.workbench.read', entryDependencies: [], parentPosition: null },
  { position: 'save', title: '保存', behaviorKey: 'authorization.workbench.save', entryDependencies: ['read'], parentPosition: 'read' }];

it('当前语言重译目录、草稿及已成功提示而不重放保存', async () => {
  const entries = JSON.parse(roleDictionary).RoleView as Record<string, Record<string, string>>;
  const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(Object.entries(entries).map(([key, values]) => [`RoleView.${key}`, values[language]]))]));
  fetcher.mockImplementation((url: string, init?: RequestInit) => Promise.resolve(init?.method === 'PUT' ? new Response(null, { status: 204 }) : json(url.endsWith('/catalog')
    ? { points: [{ ...points[0], titleKey: 'RoleView.WorkbenchTitle' }, points[1]] }
    : url.includes('/roles?') ? { items: [], total: 0 } : { role: { ...role('one'), displayName: '业务角色' }, positions: [] })));
  function Driver() { const { currentLanguage, setLanguage } = useLocalization(); return <button onClick={() => setLanguage(currentLanguage === 'en' ? 'zhs' : 'en')}>switch</button>; }
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider catalogs={catalogs} defaultLanguage="zhs" initialLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]}><Driver /><FeatureWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={(...state) => { lifecycle = state; }} /></LocalizationProvider>));
    expect(container.querySelector('.ant-empty-description')?.textContent).toBe('暂无数据');
    expect(container.querySelector('.ant-pagination-prev')?.getAttribute('title')).toBe('上一页');
    await act(async () => container.querySelector<HTMLElement>('.ant-tree-checkbox')!.click());
    const roleSearch = container.querySelector<HTMLInputElement>('[aria-label="搜索角色"]')!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(roleSearch, 'empty search');
      roleSearch.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
    expect(container.querySelector('.ant-tree-title')?.textContent).toBe('Feature authorization');
    expect(container.querySelector('.ant-empty-description')?.textContent).toBe('No data');
    expect(container.querySelector('.ant-pagination-prev')?.getAttribute('title')).toBe('Previous Page');
    expect(container.querySelector('[aria-label="Search roles"]')).toBe(roleSearch);
    expect(roleSearch.value).toBe('empty search');
    expect(container.textContent).toContain('Granted 1 / 2');
    expect(container.textContent).toContain('业务角色');
    expect(lifecycle[0].dirty).toBe(true);
    await act(async () => { expect(await lifecycle[2]!()).toBe(true); });
    expect(document.body.textContent).toContain('Authorization saved');
    await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
    expect(document.body.textContent).toContain('授权已保存');
    expect(lifecycle[0].dirty).toBe(false);
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(1);
    expect(fetcher.mock.calls.filter(call => String(call[0]).endsWith('/catalog'))).toHaveLength(1);
  } finally { await act(async () => root.unmount()); container.remove(); localStorage.clear(); }
});

it('preserves draft after unknown save and failed role load, and does not refetch catalog or repeat submission', async () => {
  let finish!: () => void;
  fetcher.mockImplementation((url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') return new Promise((_resolve, reject) => { finish = () => reject(new TypeError('offline')); });
    if (url.endsWith('/catalog')) return Promise.resolve(json({ points, canSave: true }));
    if (url.includes('/roles?')) return Promise.resolve(json({ items: ['one', 'two'].map(id => ({ EID: id, ECODE: id, ENAME: id, IsEnabled: true })), total: 2 }));
    if (url.endsWith('/one')) return Promise.resolve(json({ role: role('one'), positions: [] }));
    return Promise.reject(new Error('load failed'));
  });
  let lifecycle!: Parameters<PageProps['reportState']>;
  const reportState: PageProps['reportState'] = (...value) => { lifecycle = value; };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const click = async (text: string) => {
    const button = [...document.querySelectorAll('button')].find(item => item.textContent?.replace(/\s/g, '') === text);
    expect(button).toBeDefined(); await act(async () => button!.click());
  };
  try {
    await act(async () => root.render(<TestWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
    await act(async () => container.querySelector<HTMLElement>('.ant-tree-checkbox')!.click());
    expect(lifecycle[0].dirty).toBe(true);
    access.positions = ['read'];
    await act(async () => root.render(<TestWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
    expect([...container.querySelectorAll('button')].find(button => button.textContent === '保存授权')!.disabled).toBe(true);
    expect(lifecycle[0].dirty).toBe(true);
    access.positions = ['read', 'save'];
    await act(async () => root.render(<TestWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
    expect([...container.querySelectorAll('button')].find(button => button.textContent === '保存授权')!.disabled).toBe(false);
    let saved!: Promise<boolean>;
    await act(async () => { saved = lifecycle[2]!(); });
    await act(async () => { void lifecycle[2]!(); });
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(1);
    await act(async () => { finish(); await saved; });
    expect(container.textContent).toContain('操作结果未知');
    expect(lifecycle[0].dirty).toBe(true);
    await click('two·two'); await click('取消');
    expect(lifecycle[0].dirty).toBe(true);
    await click('two·two'); await click('放弃并切换');
    expect(container.textContent).toContain('授权数据读取失败');
    expect(lifecycle[0].dirty).toBe(true);
    expect(container.textContent).toContain('one · one');
    expect(fetcher.mock.calls.filter(call => String(call[0]).endsWith('/catalog'))).toHaveLength(1);
    await click('重置'); expect(lifecycle[0].dirty).toBe(false);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('discards a late response after switching to another role', async () => {
  let finish!: (value: Response) => void;
  fetcher.mockImplementation((url: string) => {
    if (url.endsWith('/catalog')) return Promise.resolve(json({ points, canSave: true }));
    if (url.includes('/roles?')) return Promise.resolve(json({ items: ['one', 'two'].map(id => ({ EID: id, ECODE: id, ENAME: id, IsEnabled: true })), total: 2 }));
    if (url.endsWith('/one')) return new Promise(resolve => { finish = resolve; });
    return Promise.resolve(json({ role: role('two'), positions: [] }));
  });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<TestWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={() => undefined} />));
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect([...container.querySelectorAll('button')].some(button => button.textContent?.replace(/\s/g, '') === 'two·two')).toBe(true);
    });
    const target = [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === 'two·two')!;
    await act(async () => target.click());
    await act(async () => finish(json({ role: role('one'), positions: ['read'] })));
    expect(container.textContent).toContain('two · two');
    expect([...container.querySelectorAll('.ant-card-head-title')].filter(title => title.textContent === 'two · two')).toHaveLength(1);
    expect([...container.querySelectorAll('.ant-card-head-title')].some(title => title.textContent === 'one · one')).toBe(false);
    expect(fetcher.mock.calls.filter(call => String(call[0]).endsWith('/catalog'))).toHaveLength(1);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('asks for missing entry selections and blocks saving an isolated action', async () => {
  fetcher.mockImplementation((url: string) => Promise.resolve(json(url.endsWith('/catalog') ? { points }
    : url.includes('/roles?') ? { items: [], total: 0 } : { role: role('one'), positions: [] })));
  let lifecycle!: Parameters<PageProps['reportState']>;
  const reportState: PageProps['reportState'] = (...state) => { lifecycle = state; };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<TestWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '展开全部')!.click());
    await act(async () => (container.querySelectorAll('.ant-tree-checkbox')[1] as HTMLElement).click());
    expect(container.textContent).toContain('请先补选入口：读取');
    expect([...container.querySelectorAll('button')].find(button => button.textContent === '保存授权')!.disabled).toBe(true);
    await act(async () => { expect(await lifecycle[2]!()).toBe(false); });
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(0);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('shows distinct codes for same-name roles before selection', async () => {
  fetcher.mockImplementation((url: string) => Promise.resolve(json(url.endsWith('/catalog') ? { points } : {
    items: ['TEAM-A', 'TEAM-B'].map(code => ({ EID: code, ECODE: code, ENAME: '同名角色', IsEnabled: true })), total: 2,
  })));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<TestWorkbench input={{ parameters: { featurePosition: 'read' } }} reportState={() => {}} />));
    const labels = [...container.querySelectorAll('.ant-list-item button')].map(button => button.textContent);
    expect(labels).toEqual(['同名角色 · TEAM-A ', '同名角色 · TEAM-B ']);
    expect(fetcher.mock.calls.some(([url]) => /\/roles\/[^?]+$/.test(String(url)))).toBe(false);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

function TestWorkbench(props: PageProps) {
  const entries = { ...Object.fromEntries(Object.entries(JSON.parse(roleDictionary).RoleView).map(([key, value]) => ['RoleView.' + key, value])), ...Object.fromEntries(Object.entries(JSON.parse(messageDictionary).AuthorizationMessages).map(([key, value]) => ['AuthorizationMessages.' + key, value])) } as Record<string, Record<string, string>>;
  const catalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, Object.fromEntries(Object.entries(entries).map(([key, value]) => [key, value[language]]))]));
  return <LocalizationProvider catalogs={catalogs} initialLanguage='zhs' defaultLanguage='zhs' supportedLanguages={[{ code: 'zhs', displayName: '中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]}><FeatureWorkbench {...props} /></LocalizationProvider>;
}
