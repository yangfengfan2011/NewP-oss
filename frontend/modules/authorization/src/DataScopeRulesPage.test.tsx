import { localizeDataScope } from './__tests__/dataScopeLocalization';
// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import type { PageProps } from '@icp-platform/sdk';
import DataScopeRulesPage from './DataScopeRulesPage';

const fetcher = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', async importOriginal => ({
  ...await importOriginal<typeof import('@icp-platform/core')>(),
  platformFetch: (url: string, init?: RequestInit) => url.startsWith('/plugins/')
    ? Promise.resolve(new Response(JSON.stringify({ modules: [] }))) : fetcher(url, init),
  useAuthorization: () => ({ hasFeature: () => true }),
}));
(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);
afterEach(() => { vi.clearAllMocks(); });
const clickText = async (label: string, selector = 'button') => {
  const item = [...document.querySelectorAll(selector)].find(item => item.textContent?.replace(/\s/g, '') === label.replace(/\s/g, '')) as HTMLElement;
  if (!item) throw new Error(`未找到 ${label}`);
  await act(async () => item.click());
};
const subjectsTab = async () => {
  await act(async () => (document.querySelector('[role="tab"][id$="subjects"]') as HTMLElement).click());
};
const nodeAction = async (label: string) => {
  await clickText('节点操作 ▾');
  await clickText(label, '[role="menuitem"]');
};
const addSubject = async () => { await subjectsTab(); await clickText('添加主体项'); await clickText('确定'); };
const addCondition = async () => { await clickText('新建条件'); await clickText('确定'); };

it('saves row order as unique priorities and restores it from an unordered response', async () => {
  const catalog = { actions: [{ key: 'read', title: '读取', enabled: true }],
    subjectTypes: [{ key: 'all', title: '所有用户', requiresId: false }],
    features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
    resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' }, fields: [{ name: 'ENAME', type: 'String' }] }] };
  let persisted = { id: 'order', rootId: 'order', parentId: null, name: '主体顺序', enabled: true, resourceKey: 'organization.person',
    condition: { type: 'attribute', field: 'ENAME', operator: 'equal', value: '甲' }, children: [],
    subjects: [{ id: 'low', type: 'all', subjectId: null, priority: 0, actions: { read: 'deny' } },
      { id: 'high', type: 'all', subjectId: null, priority: 1, actions: { read: 'allow' } }] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') { persisted = JSON.parse(String(init.body)); return Promise.resolve(new Response(null, { status: 204 })); }
    return Promise.resolve(new Response(JSON.stringify(url.endsWith('/catalog') ? catalog : [{ ...persisted,
      subjects: [...persisted.subjects].sort((a, b) => a.priority - b.priority) }])));
  });
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); let root = createRoot(container);
  const mount = async () => {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={(...value) => { lifecycle = value; }} />)));
    await act(async () => (container.querySelector('.ant-tree-title') as HTMLElement).click());
    await subjectsTab();
  };
  try {
    await mount();
    expect(container.querySelector('[aria-label="主体授权项 1"] [aria-label="读取：同意"]')).not.toBeNull();
    await clickText('下移', '[aria-label="主体授权项 1"] button');
    expect(lifecycle[0].dirty).toBe(true);
    await act(async () => { expect(await lifecycle[2]!()).toBe(true); });
    expect(persisted.subjects.map(subject => [subject.id, subject.priority])).toEqual([['low', 1], ['high', 0]]);
    await act(async () => root.unmount()); root = createRoot(container);
    await mount();
    expect(container.querySelector('[aria-label="主体授权项 1"] [aria-label="读取：拒绝"]')).not.toBeNull();
    expect(container.querySelector('[aria-label="主体授权项 2"] [aria-label="读取：同意"]')).not.toBeNull();
    expect(lifecycle[0].dirty).toBe(false);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('offers explicit rename, allocates unused names and blocks duplicate saves', async () => {
  const saved = { id: 'existing', rootId: 'existing', parentId: null, name: '新规则', enabled: true,
    resourceKey: 'organization.person', condition: { type: 'attribute', field: 'ENAME', operator: 'equal', value: '' }, subjects: [], children: [] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => Promise.resolve(init?.method === 'PUT'
    ? new Response(null, { status: 204 }) : new Response(JSON.stringify(url.endsWith('/catalog')
      ? { features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }], resources: [
        { resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' }, fields: [{ name: 'ENAME', type: 'String' }] }] }
      : [saved]))));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  let lifecycle!: Parameters<PageProps['reportState']>;
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={(...value) => { lifecycle = value; }} />)));
    await act(async () => (container.querySelector('.ant-tree-title') as HTMLElement).click());
    expect(container.querySelector('[aria-label="规则名称"]')).toBeNull();
    expect(container.querySelector('[aria-label="编辑规则名称"]')?.textContent).toBe('新规则');
    await clickText('＋ 新增规则');
    let input = container.querySelector('[aria-label="规则名称"]') as HTMLInputElement;
    expect(input.value).toBe('新规则2');
    expect(document.activeElement).toBe(input);
    expect(input.selectionEnd! - input.selectionStart!).toBe(input.value.length);
    await act(async () => input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 13, bubbles: true })));
    expect(container.querySelector('[aria-label="规则名称"]')).toBeNull();
    await act(async () => (container.querySelector('[aria-label="编辑规则名称"]') as HTMLElement).click());
    input = container.querySelector('[aria-label="规则名称"]') as HTMLInputElement;
    expect(document.activeElement).toBe(input);
    const rename = async (name: string) => act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, name);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await rename(' 新规则 ');
    await act(async () => { expect(await lifecycle[2]!()).toBe(false); });
    expect(fetcher.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false);
    expect(container.textContent).toContain('同级规则名称');
    await rename(' 修改名称 ');
    await act(async () => { expect(await lifecycle[2]!()).toBe(true); });
    const write = fetcher.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(JSON.parse(write![1].body).name).toBe('修改名称');
    expect(container.querySelector('[aria-label="规则名称"]')).toBeNull();
    await act(async () => (container.querySelector('[aria-label="编辑规则名称"]') as HTMLElement).click());
    input = container.querySelector('[aria-label="规则名称"]') as HTMLInputElement;
    await act(async () => input.blur());
    expect(container.querySelector('[aria-label="规则名称"]')).toBeNull();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('shows every root in one tree without pagination controls', async () => {
  const rules = Array.from({ length: 21 }, (_, index) => ({ id: `root-${index}`, rootId: `root-${index}`, parentId: null, name: `规则${index}`,
    enabled: true, resourceKey: 'organization.person', condition: { type: 'attribute', field: 'ENAME', operator: 'equal', value: '甲' }, subjects: [], children: [] }));
  fetcher.mockImplementation((url: string) => Promise.resolve(new Response(JSON.stringify(url.endsWith('/catalog') ? {
    actions: [], features: [], resources: [],
  } : url.endsWith('page=1') ? rules.slice(0, 20) : rules.slice(20)))));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={() => {}} />)));
    expect(container.querySelectorAll('.ant-tree-title')).toHaveLength(21);
    expect(container.textContent).toContain('规则20');
    expect(container.textContent).not.toContain('上一页');
    expect(container.textContent).not.toContain('下一页');
    expect(fetcher.mock.calls.filter(call => String(call[0]).includes('/rules?'))).toHaveLength(2);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('cycles each opinion independently, preserves it across tabs and only persists on root save', async () => {
  const rule = { id: 'root', rootId: 'root', parentId: null, name: '三态规则', enabled: true, resourceKey: 'organization.person',
    condition: { type: 'attribute', field: 'ENAME', operator: 'equal', value: '甲' },
    subjects: [{ id: 'all', type: 'all', subjectId: null, priority: 3, actions: { read: 'unset', edit: 'deny' } }], children: [] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => Promise.resolve(init?.method === 'PUT'
    ? new Response(null, { status: 204 }) : new Response(JSON.stringify(url.endsWith('/catalog') ? {
      actions: [{ key: 'read', title: '读取', enabled: true }, { key: 'edit', title: '编辑', enabled: true }],
      features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
      resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' }, fields: [{ name: 'ENAME', type: 'String' }] }],
    } : [rule]))));
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={(...value) => { lifecycle = value; }} />)));
    await act(async () => (container.querySelector('.ant-tree-title') as HTMLElement).click());
    await subjectsTab();
    for (const [before, after] of [['不选', '同意'], ['同意', '拒绝'], ['拒绝', '不选'], ['不选', '同意']]) {
      await act(async () => (container.querySelector(`[aria-label="读取：${before}"]`) as HTMLElement).click());
      expect(container.querySelector(`[aria-label="读取：${after}"]`)).not.toBeNull();
      expect(container.querySelector('[aria-label="编辑：拒绝"]')).not.toBeNull();
    }
    await clickText('规则条件', '[role="tab"]');
    await subjectsTab();
    expect(container.querySelector('[aria-label="读取：同意"]')).not.toBeNull();
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PUT')).toHaveLength(0);
    await act(async () => { expect(await lifecycle[2]!()).toBe(true); });
    expect(JSON.parse(String(fetcher.mock.calls.find(call => call[1]?.method === 'PUT')![1].body)).subjects[0])
      .toEqual({ ...rule.subjects[0], priority: 0, actions: { read: 'allow', edit: 'deny' } });
    await clickText('添加主体项');
    await act(async () => { expect(await lifecycle[2]!()).toBe(false); });
    await clickText('取消');
    expect(lifecycle[0].dirty).toBe(false);
    await clickText('规则条件', '[role="tab"]');
    await clickText('新建条件');
    await clickText('取消');
    expect(lifecycle[0].dirty).toBe(false);
    expect(container.querySelectorAll('[aria-label^="条件项 "]')).toHaveLength(1);
  } finally { await act(async () => root.unmount()); container.remove(); }
});


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
      items: url.includes('/department?') ? [{ id: 'other-department', name: '最小目录名称', path: [{ type: 'company', id: 'other-company', name: '乙公司' }] }] : [],
      hasMore: false, currentValues: [{ id: url.includes('/company?') ? 'company-id' : 'department-id', name: '最小目录名称',
        path: url.includes('/department?') ? [{ type: 'company', id: 'company-id', name: '甲公司' }, { type: 'department', id: 'parent-id', name: '总部' }] : null }],
    } : [rule];
    return Promise.resolve(new Response(JSON.stringify(result)));
  });
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={(...value) => { lifecycle = value; }} />)));
    await act(async () => (container.querySelector('.ant-tree-title') as HTMLElement).click());
    expect(container.querySelector('[aria-label="条件项 1"]')).not.toBeNull();
    expect(container.textContent).toContain('当前身份值');
    await subjectsTab();
    expect(container.querySelectorAll('[aria-label^="主体授权项 "]')).toHaveLength(2);
    expect(container.textContent).toContain('最小目录名称');
    expect(container.querySelector('[aria-label="主体授权项 1"]')?.textContent).toContain('甲公司 / 总部 / 最小目录名称');
    await act(async () => ([...container.querySelectorAll('[aria-label="主体授权项 1"] button')].find(item => item.textContent === '编辑') as HTMLElement).click());
    expect(document.querySelector('[aria-label="包含下级部门"]')).not.toBeNull();
    const subjectSelector = document.querySelector('.ant-drawer [aria-label="主体选择"]')?.closest('.ant-select')?.querySelector('.ant-select-selector');
    await act(async () => { subjectSelector!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })); });
    expect([...document.querySelectorAll('.ant-select-item-option-content')].map(item => item.textContent)).toContain('乙公司 / 最小目录名称');
    await clickText('取消');
    expect(lifecycle[0].dirty).toBe(false);
    await addSubject();
    expect(lifecycle[0].dirty).toBe(true);
    await act(async () => { expect(await lifecycle[2]!()).toBe(true); });
    const body = JSON.parse(String(fetcher.mock.calls.find(call => call[1]?.method === 'PUT')![1].body));
    expect(body.condition).toEqual(rule.condition);
    expect(body.subjects.slice(0, 2)).toEqual([{ ...rule.subjects[1], priority: 2 }, { ...rule.subjects[0], priority: 1 }]);
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
      resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' }, fields: [{ name: 'ENAME', type: 'String' }] }],
    } : [])));
  });
  let lifecycle!: Parameters<PageProps['reportState']>;
  const reportState: PageProps['reportState'] = (...value) => { lifecycle = value; };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }} reportState={reportState} />)));
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent?.includes('新增规则'))!.click());
    await subjectsTab();
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

it('keeps the draft and places a server error at the nested condition named in Problem Details', async () => {
  const rule = { id: 'nested', rootId: 'nested', parentId: null, name: '嵌套', enabled: true, resourceKey: 'organization.person',
    condition: { type: 'group', field: '', operator: 'and', value: null,
      children: [{ type: 'attribute', field: 'ENAME', operator: 'equal', value: '甲' }] },
    subjects: [], children: [] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => Promise.resolve(init?.method === 'PUT'
    ? new Response(JSON.stringify({ title: 'diagnostic only', code: 'DataScope.InvalidConfiguration', reasonKey: 'AuthorizationErrors.DATA_SCOPE_VALUE_TYPE_MISMATCH', args: { conditionPath: '1.2' } }), { status: 400 })
    : new Response(JSON.stringify(url.endsWith('/catalog') ? {
      actions: [{ key: 'read', enabled: true }], features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
      resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' },
        fields: [{ name: 'ENAME', type: 'String' }] }],
    } : [rule]))));
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }}
      reportState={(...value) => { lifecycle = value; }} />)));
    await act(async () => (container.querySelector('.ant-tree-title') as HTMLElement).click());
    await addCondition();
    await act(async () => { expect(await lifecycle[2]!()).toBe(false); });
    expect(lifecycle[0].dirty).toBe(true);
    expect(container.querySelector('[aria-label="条件项 1.2"] .ant-alert')).not.toBeNull();
    expect(container.querySelector('[aria-label="条件项 1.1"] .ant-alert')).toBeNull();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('edits nested nodes in one root draft and sends the complete tree on save', async () => {
  const rule = { id: 'tree', rootId: 'tree', parentId: null, name: '父规则', enabled: true, resourceKey: 'organization.person',
    condition: { type: 'attribute', field: 'ENAME', operator: 'equal', value: '父' },
    subjects: [], children: [] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => Promise.resolve(init?.method === 'PUT'
    ? new Response(null, { status: 204 }) : new Response(JSON.stringify(url.endsWith('/catalog') ? {
      actions: [{ key: 'read', enabled: true }], features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
      resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' },
        fields: [{ name: 'ENAME', type: 'String' }] }],
    } : [rule, { ...rule, id: 'other', rootId: 'other', name: '另一根' }]))));
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }}
      reportState={(...value) => { lifecycle = value; }} />)));
    await act(async () => (container.querySelector('.ant-tree-title') as HTMLElement).click());
    await nodeAction('添加子规则');
    expect(container.textContent).toContain('完整条件还包含 1 个祖先条件');
    expect(container.textContent).toContain('父规则');
    expect(container.textContent).toContain('ENAME 等于 父');
    const childName = container.querySelector('[aria-label="规则名称"]') as HTMLInputElement;
    await act(async () => { childName.value = '例外节点'; childName.dispatchEvent(new Event('input', { bubbles: true })); });
    await addSubject();
    await act(async () => { expect(await lifecycle[2]!()).toBe(true); });
    const saved = JSON.parse(String(fetcher.mock.calls.find(call => call[1]?.method === 'PUT')![1].body));
    expect(saved.id).toBe('tree');
    expect(saved.children).toHaveLength(1);
    expect(saved.children[0].rootId).toBe('tree');
    expect(saved.children[0].parentId).toBe('tree');
    expect(saved.children[0].subjects).toHaveLength(1);
    expect(lifecycle[0].dirty).toBe(false);
    await nodeAction('添加同级规则');
    expect(lifecycle[0].dirty).toBe(true);
    await act(async () => ([...container.querySelectorAll('.ant-tree-title')].find(item => item.textContent === '另一根') as HTMLElement).click());
    expect(document.body.textContent).toContain('放弃未保存的草稿？');
    await act(async () => ([...document.querySelectorAll('.ant-modal button')].find(item => item.textContent === '继续编辑') as HTMLElement).click());
    await nodeAction('删除节点');
    await clickText('删除节点', '.ant-modal button');
    expect(lifecycle[0].dirty).toBe(false);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('selects the failing child and keeps its condition error inline', async () => {
  const child = { id: 'child', rootId: 'tree', parentId: 'tree', name: '子节点', enabled: true,
    resourceKey: 'organization.person', condition: { type: 'group', field: '', operator: 'and', value: null,
      children: [{ type: 'attribute', field: 'ENAME', operator: 'equal', value: '甲' }] }, subjects: [], children: [] };
  const rootRule = { ...child, id: 'tree', parentId: null, name: '根节点',
    condition: { type: 'group', field: '', operator: 'and', value: null, children: [
      { type: 'attribute', field: 'EID', operator: 'equal', value: null, contextKey: 'current.person' },
      { type: 'business', field: '', operator: '', value: null, key: 'organization.person.departmentEnabled' },
    ] }, children: [child] };
  fetcher.mockImplementation((url: string, init?: RequestInit) => Promise.resolve(init?.method === 'PUT'
    ? new Response(JSON.stringify({ title: 'diagnostic only', code: 'DataScope.InvalidConfiguration', reasonKey: 'AuthorizationErrors.DATA_SCOPE_VALUE_TYPE_MISMATCH', args: { nodeId: 'child', conditionPath: '1.2' } }), { status: 400 })
    : new Response(JSON.stringify(url.endsWith('/catalog') ? {
      actions: [{ key: 'read', enabled: true }], features: [{ behaviorKey: 'authorization.dataScope.save', position: 'save' }],
      contextValues: [{ key: 'current.person', title: '当前人员', valueType: 'String' }],
      businessConditions: [{ key: 'organization.person.departmentEnabled', title: '人员所属部门已启用',
        modelKeys: ['Organization/Person'], hasReadContribution: false }],
      resources: [{ resource: { resourceKey: 'organization.person', modelName: 'Person', modelKey: 'Organization/Person' },
        fields: [{ name: 'ENAME', type: 'String' }, { name: 'EID', type: 'String' }] }],
    } : [rootRule]))));
  let lifecycle!: Parameters<PageProps['reportState']>;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeRulesPage input={{ parameters: { featurePosition: 'read' } }}
      reportState={(...value) => { lifecycle = value; }} />)));
    await act(async () => (container.querySelectorAll('.ant-tree-title')[0] as HTMLElement).click());
    await act(async () => (container.querySelectorAll('.ant-tree-title')[1] as HTMLElement).click());
    await addCondition();
    await act(async () => (container.querySelectorAll('.ant-tree-title')[0] as HTMLElement).click());
    await act(async () => { expect(await lifecycle[2]!()).toBe(false); });
    expect(container.querySelector('[aria-label="规则路径"]')?.textContent).toContain('根节点 /');
    expect(container.querySelector('[aria-label="编辑规则名称"]')?.textContent).toBe('子节点');
    expect(container.textContent).toContain('EID 等于 当前身份值：当前人员');
    expect(container.textContent).toContain('人员所属部门已启用（无读取贡献）');
    expect(container.textContent).toContain('且 AND');
    expect(container.querySelector('[aria-label="条件项 1.2"] .ant-alert')).not.toBeNull();
  } finally { await act(async () => root.unmount()); container.remove(); }
});
