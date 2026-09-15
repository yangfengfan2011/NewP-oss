// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import type { PageProps } from '@icp-platform/sdk';
import FeatureWorkbench from './FeatureWorkbench';

const fetcher = vi.hoisted(() => vi.fn());
const access = vi.hoisted(() => ({ positions: ['read', 'save'] }));
vi.mock('@icp-platform/core', () => ({ platformFetch: fetcher, useAuthorization: () => ({ hasFeature: (position: string) => access.positions.includes(position) }) }));
(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);
afterEach(() => { vi.clearAllMocks(); access.positions = ['read', 'save']; });
const json = (value: unknown) => new Response(JSON.stringify(value));
const role = (id: string) => ({ id, code: id, displayName: id, isEnabled: true });
const points = [{ position: 'read', title: '读取', behaviorKey: 'authorization.workbench.read', entryDependencies: [], parentPosition: null },
  { position: 'save', title: '保存', behaviorKey: 'authorization.workbench.save', entryDependencies: ['read'], parentPosition: 'read' }];

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
    await act(async () => root.render(<FeatureWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
    await act(async () => container.querySelector<HTMLElement>('.ant-tree-checkbox')!.click());
    expect(lifecycle[0].dirty).toBe(true);
    access.positions = ['read'];
    await act(async () => root.render(<FeatureWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
    expect([...container.querySelectorAll('button')].find(button => button.textContent === '保存授权')!.disabled).toBe(true);
    expect(lifecycle[0].dirty).toBe(true);
    access.positions = ['read', 'save'];
    await act(async () => root.render(<FeatureWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
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
    expect(container.textContent).toContain('角色读取失败');
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
    await act(async () => root.render(<FeatureWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={() => undefined} />));
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
    await act(async () => root.render(<FeatureWorkbench input={{ parameters: { featurePosition: 'read', roleId: 'one' } }} reportState={reportState} />));
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
    await act(async () => root.render(<FeatureWorkbench input={{ parameters: { featurePosition: 'read' } }} reportState={() => {}} />));
    const labels = [...container.querySelectorAll('.ant-list-item button')].map(button => button.textContent);
    expect(labels).toEqual(['同名角色 · TEAM-A ', '同名角色 · TEAM-B ']);
    expect(fetcher.mock.calls.some(([url]) => /\/roles\/[^?]+$/.test(String(url)))).toBe(false);
  } finally { await act(async () => root.unmount()); container.remove(); }
});