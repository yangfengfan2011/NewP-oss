// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import type { PageProps } from '@icp-platform/sdk';
import DataScopeRulesPage from './DataScopeRulesPage';

const fetcher = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', () => ({ platformFetch: fetcher, useAuthorization: () => ({ hasFeature: () => true }) }));
(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);
afterEach(() => { vi.clearAllMocks(); });

it('keeps stable root and subject identities after failed save and distinguishes disabled action from opinion', async () => {
  let fail!: () => void;
  fetcher.mockImplementation((url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') return new Promise((_resolve, reject) => { fail = () => reject(new TypeError('offline')); });
    return Promise.resolve(new Response(JSON.stringify(url.endsWith('/catalog') ? {
      actions: [{ key: 'read', title: '读取', enabled: false }],
      features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
      resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person' }, fields: [{ name: 'ENAME', type: 'String' }] }],
    } : [])));
  });
  let lifecycle!: Parameters<PageProps['reportState']>;
  const reportState: PageProps['reportState'] = (...value) => { lifecycle = value; };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={reportState} />));
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '新增规则')!.click());
    expect(container.textContent).toContain('目录已停用');
    expect(container.textContent).toContain('不选');
    expect(lifecycle[0].dirty).toBe(true);
    let pending!: Promise<boolean>;
    await act(async () => { pending = lifecycle[2]!(); });
    await act(async () => { void lifecycle[2]!(); });
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(1);
    const first = JSON.parse(String(fetcher.mock.calls.find(call => call[1]?.method === 'PUT')![1].body));
    expect(first.rootId).toBe(first.id);
    expect(first.subjects[0].actions).toEqual({ read: 'unset' });
    await act(async () => { fail(); await pending; });
    expect(lifecycle[0].dirty).toBe(true);
    expect(container.textContent).toContain('不会自动重试');
    await act(async () => { pending = lifecycle[2]!(); });
    const attempts = fetcher.mock.calls.filter(call => call[1]?.method === 'PUT');
    expect(JSON.parse(String(attempts[1][1].body))).toEqual(first);
    await act(async () => { fail(); await pending; });
  } finally { await act(async () => root.unmount()); container.remove(); }
});
