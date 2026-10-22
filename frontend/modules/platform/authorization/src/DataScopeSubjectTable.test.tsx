import { localizeDataScope } from './__tests__/dataScopeLocalization';
// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import DataScopeSubjectTable from './DataScopeSubjectTable';
import type { DataScopeSubject } from './DataScopeSubjectEditor';

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);

it('uses saved priority order, moves rows, appends at the bottom and hides numeric priority in the editor', async () => {
  const original = [
    { id: 'b', type: 'user', subjectId: 'B', priority: 1, actions: { read: 'deny' } },
    { id: 'a', type: 'user', subjectId: 'A', priority: 2, actions: { read: 'allow' } },
  ];
  let current: DataScopeSubject[] = original;
  const read = vi.fn(async () => ({ items: [], hasMore: false, currentValues: [{ id: 'A', name: '甲用户' }, { id: 'B', name: '乙用户' }] }));
  function Harness() {
    const [subjects, setSubjects] = useState(current);
    return <DataScopeSubjectTable subjects={subjects} types={[{ key: 'user', title: '用户', requiresId: true }, { key: 'all', title: '所有用户', requiresId: false }]}
      actions={[{ key: 'read', title: '读取', enabled: true }]} read={read} disabled={false}
      onChange={next => { current = next; setSubjects(next); }} onEditingChange={() => {}} />;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const click = async (text: string, selector = 'button') => {
    const button = [...document.querySelectorAll(selector)].find(item => item.textContent?.replace(/\s/g, '') === text) as HTMLElement;
    expect(button, text).toBeDefined(); await act(async () => button.click());
  };
  try {
    await act(async () => root.render(localizeDataScope(<Harness />)));
    expect(container.querySelector('[aria-label="主体授权项 1"]')?.textContent).toContain('甲用户');
    expect(container.querySelectorAll('th')[0].textContent).toBe('授权主体');
    expect(container.textContent).not.toContain('主体类型');
    expect(container.textContent).not.toContain('主体名称');
    expect(container.textContent).not.toContain('优先级');
    await click('上移', '[aria-label="主体授权项 2"] button');
    expect(current.map(item => [item.id, item.priority])).toEqual([['b', 1], ['a', 0]]);
    expect(current.map(item => item.actions.read)).toEqual(['deny', 'allow']);
    await click('下移', '[aria-label="主体授权项 1"] button');
    expect(current.map(item => [item.id, item.priority])).toEqual([['a', 1], ['b', 0]]);
    await click('添加主体项');
    expect(document.querySelector('.ant-drawer [aria-label="优先级"]')).toBeNull();
    await click('确定', '.ant-drawer button');
    expect(current.map(item => item.priority)).toEqual([2, 1, 0]);
    expect(current[2].type).toBe('all');
    expect(container.querySelector('[aria-label="主体授权项 3"]')?.textContent?.match(/所有用户/g)).toHaveLength(1);
    await click('删除', '[aria-label="主体授权项 2"] button');
    await click('删除', '.ant-popconfirm button');
    expect(current.map(item => item.priority)).toEqual([1, 0]);
    expect(current[0].id).toBe('a');
    expect(current[1].type).toBe('all');
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('shows all subjects without pagination and moves within the complete order', async () => {
  let current: DataScopeSubject[] = Array.from({ length: 11 }, (_, index) => ({ id: `item-${index}`, type: 'all', subjectId: null, priority: 10 - index, actions: {} }));
  function Harness() {
    const [subjects, setSubjects] = useState(current);
    return <DataScopeSubjectTable subjects={subjects} types={[{ key: 'all', title: '所有用户', requiresId: false }]} actions={[]}
      read={vi.fn()} disabled={false} onChange={next => { current = next; setSubjects(next); }} onEditingChange={() => {}} />;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<Harness />)));
    await act(async () => ([...container.querySelectorAll('[aria-label="主体授权项 10"] button')].find(button => button.textContent === '下移') as HTMLElement).click());
    expect(current.slice(9).map(subject => subject.id)).toEqual(['item-10', 'item-9']);
    expect(current.map(subject => subject.priority)).toEqual([10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0]);
    expect(container.querySelectorAll('tbody tr[data-row-key]')).toHaveLength(11);
    expect(container.querySelector('.ant-pagination')).toBeNull();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('renders all subjects while batching name reads and cancels obsolete requests', async () => {
  const subjects = Array.from({ length: 21 }, (_, index) => ({ id: `item-${index}`, type: 'person', subjectId: `person-${index}`, priority: 0, actions: {} }));
  const pending: { path: string; signal?: AbortSignal; resolve: (value: unknown) => void }[] = [];
  const read = vi.fn((path: string, signal?: AbortSignal) => new Promise(resolve => pending.push({ path, signal, resolve })));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const render = (items: DataScopeSubject[]) => <DataScopeSubjectTable subjects={items} types={[{ key: 'person', title: '人员', requiresId: true }]}
    actions={[]} read={read} disabled={false} onChange={() => {}} onEditingChange={() => {}} />;
  try {
    await act(async () => root.render(localizeDataScope(render(subjects))));
    expect(container.querySelectorAll('tbody tr[data-row-key]')).toHaveLength(21);
    expect(container.querySelector('.ant-pagination')).toBeNull();
    expect(pending).toHaveLength(1);
    expect(new URLSearchParams(pending[0].path.split('?')[1]).getAll('currentIds')).toEqual(subjects.slice(0, 10).map(subject => subject.subjectId));
    await act(async () => pending[0].resolve({ currentValues: [] }));
    expect(pending).toHaveLength(2);
    expect(new URLSearchParams(pending[1].path.split('?')[1]).getAll('currentIds')).toEqual(subjects.slice(10, 20).map(subject => subject.subjectId));
    await act(async () => root.render(localizeDataScope(render([{ ...subjects[0], subjectId: 'new-person' }]))));
    expect(pending[1].signal?.aborted).toBe(true);
    await act(async () => pending[2].resolve({ currentValues: [{ id: 'new-person', name: '当前人员' }] }));
    await act(async () => pending[1].resolve({ currentValues: [{ id: 'person-10', name: '过期人员' }] }));
    expect(pending).toHaveLength(3);
    expect(container.textContent).toContain('当前人员');
    expect(container.textContent).not.toContain('过期人员');
  } finally { await act(async () => root.unmount()); container.remove(); }
  expect(pending[2].signal?.aborted).toBe(true);
});

it('makes opinions and mutation controls unavailable in read-only mode', async () => {
  const onChange = vi.fn();
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(localizeDataScope(<DataScopeSubjectTable subjects={[{ id: 'all', type: 'all', subjectId: null, priority: 0, actions: { read: 'deny' } }]}
      types={[{ key: 'all', title: '所有用户', requiresId: false }]} actions={[{ key: 'read', title: '读取', enabled: true }]}
      read={vi.fn()} disabled onChange={onChange} onEditingChange={() => {}} />)));
    const opinion = container.querySelector('[role="checkbox"]') as HTMLButtonElement;
    expect(opinion.disabled).toBe(true);
    expect(opinion.getAttribute('aria-label')).toBe('读取：拒绝');
    await act(async () => opinion.click());
    expect(onChange).not.toHaveBeenCalled();
    expect([...container.querySelectorAll('button')].every(button => button.disabled)).toBe(true);
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('replaces names when the qualified transport changes and ignores its late response', async () => {
  let finish!: (value: unknown) => void;
  let signal: AbortSignal | undefined;
  const oldRead = (_path: string, nextSignal?: AbortSignal) => new Promise(resolve => { finish = resolve; signal = nextSignal; });
  const newRead = vi.fn(async () => ({ currentValues: [{ id: 'same', name: '新资格名称', path: [{ type: 'company', id: 'c', name: '公司' }] }] }));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const render = (read: typeof oldRead) => <DataScopeSubjectTable subjects={[{ id: 's', type: 'department', subjectId: 'same', priority: 0, actions: {} }]}
    types={[{ key: 'department', title: '部门', requiresId: true }]} actions={[]} read={read} disabled={false} onChange={() => {}} onEditingChange={() => {}} />;
  try {
    await act(async () => root.render(localizeDataScope(render(oldRead))));
    await act(async () => root.render(localizeDataScope(render(newRead))));
    expect(signal?.aborted).toBe(true);
    await act(async () => finish({ currentValues: [{ id: 'same', name: '旧资格名称' }] }));
    expect(container.textContent).toContain('公司 / 新资格名称');
    expect(container.textContent).not.toContain('旧资格名称');
    expect(newRead).toHaveBeenCalledTimes(1);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
