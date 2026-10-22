import { act } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { OverflowActionToolbar, type OverflowActionToolbarItem } from '@icp-platform/ui/crud-list';
import { createLocalizedRoot } from './localizedTestRoot';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it('moves trailing actions into More on resize, preserves nested states and restores the original order', async () => {
  let width = 300;
  let resize!: () => void;
  const disconnected = vi.fn();
  vi.stubGlobal('ResizeObserver', class {
    constructor(private callback: () => void) {}
    observe(element: HTMLElement) { if (element.classList.contains('np-action-toolbar')) resize = this.callback; }
    disconnect = disconnected;
  });
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function () {
    const measuredWidth = this.classList.contains('np-action-toolbar') ? width : this.classList.contains('np-action-toolbar-more') ? 80 : 100;
    return new DOMRect(0, 0, measuredWidth, 32);
  });
  const onAction = vi.fn();
  const items: OverflowActionToolbarItem[] = [
    { key: 'new', button: <button>新建</button>, menuItem: { key: 'new', label: '新建' } },
    { key: 'copy', button: <button>复制</button>, menuItem: { key: 'copy', label: '复制', disabled: true } },
    { key: 'manage', button: <button>管理</button>, menuItem: { key: 'manage', label: '管理', children: [
      { key: 'remove', label: '移除', danger: true }, { key: 'delete', label: '删除', disabled: true, danger: true },
    ] } },
  ];
  const container = document.createElement('div'); document.body.append(container);
  const root = createLocalizedRoot(container);
  const visible = () => [...container.querySelectorAll<HTMLElement>('.np-action-toolbar-item:not([data-overflowed])')].map(item => item.textContent);
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === label)!;
  try {
    await act(async () => root.render(<OverflowActionToolbar items={items} onAction={onAction} />));
    expect(visible()).toEqual(['新建', '复制', '管理']);
    width = 200;
    await act(async () => resize());
    expect(visible()).toEqual(['新建']);
    await act(async () => container.querySelector<HTMLButtonElement>('.np-action-toolbar-more button')!.click());
    expect(menu('复制').getAttribute('aria-disabled')).toBe('true');
    await act(async () => menu('复制').click());
    expect(onAction).not.toHaveBeenCalled();
    await act(async () => menu('管理').click());
    await vi.waitFor(async () => { await act(async () => undefined); expect(menu('删除')).toBeDefined(); });
    expect(menu('删除').getAttribute('aria-disabled')).toBe('true');
    expect(menu('移除').classList.contains('ant-dropdown-menu-item-danger')).toBe(true);
    await act(async () => menu('移除').click());
    expect(onAction).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledWith('remove');
    await act(async () => container.querySelector<HTMLButtonElement>('.np-action-toolbar-more button')!.click());
    expect(menu('复制')).toBeDefined();
    width = 400;
    await act(async () => resize());
    expect(visible()).toEqual(['新建', '复制', '管理']);
    expect(container.querySelector('.np-action-toolbar-more')?.getAttribute('aria-hidden')).toBe('true');
    expect(document.querySelector('[role="menuitem"]')).toBeNull();
    width = 60;
    await act(async () => resize());
    expect(visible()).toEqual([]);
    expect(container.querySelector('.np-action-toolbar-more')?.getAttribute('aria-hidden')).toBeNull();
  } finally {
    await act(async () => root.unmount()); container.remove();
  }
  expect(disconnected).toHaveBeenCalled();
});
