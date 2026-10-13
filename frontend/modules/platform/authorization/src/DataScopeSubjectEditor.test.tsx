import { localizeDataScope } from './__tests__/dataScopeLocalization';
// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import DataScopeSubjectEditor from './DataScopeSubjectEditor';

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });

it('keeps selected values in paged options and discards requests from the previous subject type', async () => {
  const pending: { path: string; signal?: AbortSignal; resolve: (value: unknown) => void; reject: (error: unknown) => void }[] = [];
  const read = vi.fn((path: string, signal?: AbortSignal) => new Promise((resolve, reject) => pending.push({ path, signal, resolve, reject })));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const render = (type: string) => <DataScopeSubjectEditor subject={{ id: 's', type, subjectId: 'saved', priority: 0, actions: {} }}
    index={0} types={[{ key: 'department', title: '部门', requiresId: true }, { key: 'user', title: '用户', requiresId: true }]}
    actions={[]} read={read} onChange={() => {}} />;
  try {
    await act(async () => root.render(localizeDataScope(render('department'))));
    await act(async () => root.render(localizeDataScope(render('user'))));
    expect(pending[0].signal?.aborted).toBe(true);
    expect(pending[1].path).toBe('/subjects/user?query=&page=1&currentIds=saved');
    await act(async () => pending[1].resolve({ items: [{ id: 'saved', name: '候选旧名' }, { id: 'other', name: '其他用户' }], hasMore: true,
      currentValues: [{ id: 'saved', name: '当前用户', path: [{ type: 'company', id: 'c', name: '公司' }] }] }));
    await act(async () => pending[0].resolve({ items: [{ id: 'saved', name: '过期部门' }], hasMore: false, currentValues: [] }));
    expect(container.textContent).toContain('公司 / 当前用户');
    expect(container.textContent).not.toContain('过期部门');
    const selector = container.querySelector('[aria-label="主体选择"]')!.closest('.ant-select')!.querySelector('.ant-select-selector')!;
    await act(async () => selector.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
    expect([...document.querySelectorAll('.ant-select-item-option-content')].map(item => item.textContent)).toEqual(['公司 / 当前用户', '其他用户']);
    await act(async () => ([...container.querySelectorAll('button')].find(item => item.textContent === '下一页主体') as HTMLElement).click());
    expect(pending[2].path).toBe('/subjects/user?query=&page=2&currentIds=saved');
    await act(async () => pending[2].reject(new Error('目录读取被拒绝')));
    expect(container.textContent).toContain('操作失败，请稍后重试。');
    expect(container.textContent).not.toContain('目录读取被拒绝');
    expect(container.textContent).toContain('公司 / 当前用户');
  } finally { await act(async () => root.unmount()); container.remove(); }
  expect(pending[2].signal?.aborted).toBe(true);
});
