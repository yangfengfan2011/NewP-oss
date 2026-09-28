// @vitest-environment jsdom
import { localizeDataScope } from './__tests__/dataScopeLocalization';
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import DataScopeConditionTable from './DataScopeConditionTable';
import type { DataScopeCondition } from './DataScopeConditionEditor';

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
const originalStyle = window.getComputedStyle.bind(window);
window.getComputedStyle = element => originalStyle(element);
const leaf = (value: string): DataScopeCondition => ({ type: 'attribute', field: 'ENAME', operator: 'equal', value });
const group = (operator: string, children: DataScopeCondition[]): DataScopeCondition => ({ type: 'group', field: '', operator, value: null, children });
const a = leaf('甲'), b = leaf('乙'), c = leaf('丙');
const clickText = async (label: string, selector = 'button') => {
  const item = [...document.querySelectorAll(selector)].find(item => item.textContent?.replace(/\s/g, '') === label.replace(/\s/g, '')) as HTMLElement;
  expect(item, label).toBeDefined();
  await act(async () => item.click());
};
const clickLabel = async (label: string) => {
  const item = document.querySelector(`[aria-label="${label}"]`) as HTMLElement;
  expect(item, label).not.toBeNull();
  await act(async () => item.click());
};
async function render(initial: DataScopeCondition) {
  let current = initial;
  const onEditingChange = vi.fn();
  function Harness() {
    const [condition, setCondition] = useState(initial);
    return <DataScopeConditionTable condition={condition} fields={[{ name: 'ENAME', type: 'String' }]} contexts={[]} bindings={[]}
      businessTypes={[]} modelKey="Organization/Person" disabled={false} onEditingChange={onEditingChange}
      onChange={next => { current = next; setCondition(next); }} />;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  await act(async () => root.render(localizeDataScope(<Harness />)));
  return { container, value: () => current, onEditingChange, close: async () => { await act(async () => root.unmount()); container.remove(); } };
}

it('shows an overall operator, hides the synthetic root and keeps group creation out of the condition editor', async () => {
  const ui = await render(a);
  try {
    expect(ui.container.querySelector('[aria-label="整体组合方式"]')).not.toBeNull();
    expect(ui.value()).toBe(a);
    await clickText('新建条件');
    const selector = document.querySelector('.ant-drawer [aria-label="条件类型"]')!.closest('.ant-select')!.querySelector('.ant-select-selector')!;
    await act(async () => selector.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
    expect([...document.querySelectorAll('.ant-select-item-option-content')].map(item => item.textContent)).toEqual(['属性条件', '业务条件']);
    await clickText('取消', '.ant-drawer button');
    expect(ui.value()).toBe(a);
    expect(ui.onEditingChange).toHaveBeenLastCalledWith(false);
    await clickText('新建条件');
    await clickText('确定', '.ant-drawer button');
    expect(ui.value().children?.[0]).toEqual(a);
    expect(ui.container.querySelector('[aria-label="条件项 1"]')).toBeNull();
    expect(ui.container.querySelectorAll('tbody tr[data-row-key]')).toHaveLength(2);
  } finally { await ui.close(); }
});

it('groups only selected siblings while other levels are disabled', async () => {
  const original = group('and', [a, group('or', [b, c]), leaf('丁')]);
  const ui = await render(original);
  try {
    await clickLabel('选择条件 1');
    expect((ui.container.querySelector('[aria-label="选择条件 2.1"]') as HTMLInputElement).disabled).toBe(true);
    await clickLabel('选择条件 3');
    await clickText('组合为任一满足');
    expect(ui.value()).toEqual(group('and', [group('or', [a, leaf('丁')]), original.children![1]]));
    expect(ui.container.textContent).not.toContain('已选');
  } finally { await ui.close(); }
});

it('previews semantic changes when ungrouping and preserves the draft on cancel', async () => {
  const original = group('and', [a, group('or', [b, c])]);
  const ui = await render(original);
  try {
    await clickLabel('条件 2 更多操作'); await clickText('解除分组', '[role="menuitem"]');
    expect(document.querySelector('.ant-modal')?.textContent).toContain('修改前');
    expect(document.querySelector('.ant-modal')?.textContent).toContain('修改后');
    expect(document.querySelector('.ant-modal')?.textContent).toContain('或 OR');
    expect(ui.value()).toBe(original);
    await clickText('取消', '.ant-modal button');
    expect(ui.value()).toBe(original);
    expect(ui.onEditingChange).toHaveBeenLastCalledWith(false);
    await clickLabel('条件 2 更多操作'); await clickText('解除分组', '[role="menuitem"]');
    await clickText('确认修改', '.ant-modal button');
    expect(ui.value()).toEqual(group('and', [a, b, c]));
  } finally { await ui.close(); }
});

it('moves a nested group beside its parent with a preview and preserves both groups', async () => {
  const inner = group('and', [b, c]);
  const original = group('and', [group('or', [a, inner]), leaf('丁')]);
  const ui = await render(original);
  try {
    await clickLabel('条件 1.2 更多操作'); await clickText('移出此组', '[role="menuitem"]');
    expect(ui.value()).toBe(original);
    await clickText('确认修改', '.ant-modal button');
    expect(ui.value()).toEqual(group('and', [group('or', [a]), inner, leaf('丁')]));
    await clickLabel('条件 2 更多操作'); await clickText('删除整组', '[role="menuitem"]');
    await clickText('确认修改', '.ant-modal button');
    expect(ui.value()).toEqual(group('and', [group('or', [a]), leaf('丁')]));
  } finally { await ui.close(); }
});

it('edits a nested leaf without changing the surrounding operators', async () => {
  const original = group('and', [a, group('or', [b, c])]);
  const ui = await render(original);
  try {
    await clickText('编辑', '[aria-label="条件项 1.2.1"] button');
    const input = document.querySelector('.ant-drawer [aria-label="固定值"]') as HTMLInputElement;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '丁');
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await clickText('确定', '.ant-drawer button');
    expect(ui.value()).toEqual(group('and', [a, group('or', [leaf('丁'), c])]));
  } finally { await ui.close(); }
});
