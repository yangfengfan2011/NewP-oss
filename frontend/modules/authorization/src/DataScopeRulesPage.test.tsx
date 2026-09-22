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

it('loads minimal names for multiple subjects and preserves identity context and matching options when saving', async () => {
  const rule = { id: 'root', rootId: 'root', parentId: null, name: '当前公司', enabled: true, resourceKey: 'organization.person',
    condition: { type: 'attribute', field: 'CompanyId', operator: 'equal', value: null, contextKey: 'current.company' },
    subjects: [{ id: 'company', type: 'company', subjectId: 'company-id', priority: 1, actions: { read: 'deny' } },
      { id: 'department', type: 'department', subjectId: 'department-id', priority: 20, actions: { read: 'allow' }, matchOptions: { includeDescendants: true } }], children: [] };
  const catalog = { actions: [{ key: 'read', enabled: true }], features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
    subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }, { key: 'company', title: '公司', requiresId: true },
      { key: 'department', title: '部门', requiresId: true, supportsDescendants: true }],
    contextValues: [{ key: 'current.company', title: '当前公司', valueType: 'String' }],
    contextBindings: [{ modelKey: 'Organization/Person', field: 'CompanyId', contextKey: 'current.company' }],
    resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' }, fields: [{ name: 'CompanyId', type: 'String' }] }] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') return Promise.resolve(new Response(null, { status: 204 }));
    const result = url.endsWith('/catalog') ? catalog : url.includes('/subjects/') ? {
      items: [], hasMore: false, currentValues: [{ id: url.includes('/company?') ? 'company-id' : 'department-id', name: '最小目录名称' }],
    } : [rule];
    return Promise.resolve(new Response(JSON.stringify(result)));
  });
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={(...value) => { lifecycle = value; }} />));
    await act(async () => (container.querySelector('.ant-tree-title') as HTMLElement).click());
    expect(container.querySelectorAll('[role="group"]')).toHaveLength(2);
    expect(container.querySelectorAll('[aria-label="包含下级部门"]')).toHaveLength(1);
    expect(container.textContent).toContain('最小目录名称');
    expect(container.textContent).toContain('当前身份值');
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '添加主体项')!.click());
    expect(lifecycle[0].dirty).toBe(true);
    await act(async () => { expect(await lifecycle[2]!()).toBe(true); });
    const body = JSON.parse(String(fetcher.mock.calls.find(call => call[1]?.method === 'PUT')![1].body));
    expect(body.condition).toEqual(rule.condition);
    expect(body.subjects.slice(0, 2)).toEqual(rule.subjects);
    expect(body.subjects[2].actions.read).toBe('unset');
    expect(lifecycle[0].dirty).toBe(false);
    expect(fetcher.mock.calls.filter(call => String(call[0]).includes('/subjects/')).every(call => call[1]?.signal instanceof AbortSignal)).toBe(true);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

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
