// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { ContextActionMenu } from '@icp-platform/ui/core';

// 显式推进浏览器帧，覆盖关闭后才到达的聚焦任务，不依赖运行负载或固定等待。
function controlAnimationFrames() {
  let nextId = 0;
  const pending = new Map<number, FrameRequestCallback>();
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => { const id = ++nextId; pending.set(id, callback); return id; });
  vi.stubGlobal('cancelAnimationFrame', (id: number) => pending.delete(id));
  return async () => {
    for (let frame = 0; frame < 4; frame++) {
      await act(async () => {
        const callbacks = [...pending.values()]; pending.clear();
        callbacks.forEach(callback => callback(performance.now()));
      });
    }
  };
}

it('keeps focus on the trigger when Escape closes before deferred menu autofocus', async () => {
  const advanceFrames = controlAnimationFrames();
  function Harness() {
    const [open, setOpen] = useState(false);
    return <ContextActionMenu open={open} onOpenChange={setOpen} keyboardLabel="Object actions"
      trigger={['contextMenu']} menu={{ items: [{ key: 'edit', label: 'Edit' }] }}><span>Object</span></ContextActionMenu>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<Harness />));
    const target = container.querySelector<HTMLElement>('[aria-label="Object actions"]')!;
    target.focus();
    await act(async () => target.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true })));
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })));
    expect(target.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(target);
    await advanceFrames();
    expect(document.activeElement).toBe(target);
  } finally { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); }
});

it('focuses the first enabled action, supports keyboard execution, and restores trigger focus', async () => {
  const advanceFrames = controlAnimationFrames();
  const execute = vi.fn();
  function Harness() {
    const [open, setOpen] = useState(false);
    return <ContextActionMenu open={open} onOpenChange={setOpen} keyboardLabel="Object actions"
      trigger={['contextMenu']} menu={{ items: [{ key: 'disabled', label: 'Unavailable', disabled: true }, { key: 'edit', label: 'Edit' }, { key: 'remove', label: 'Remove' }], onClick: execute }}><span>Object</span></ContextActionMenu>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<Harness />));
    const target = container.querySelector<HTMLElement>('[aria-label="Object actions"]')!;
    target.focus();
    await act(async () => target.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true })));
    expect(target.getAttribute('aria-expanded')).toBe('true');
    // jsdom 不计算布局；仅补真实菜单项的可见几何供 rc-menu 箭头导航判定。
    document.querySelectorAll<HTMLElement>('[role="menuitem"]').forEach(item => Object.defineProperty(item, 'offsetParent', { value: document.body }));
    await advanceFrames();
    expect(document.activeElement?.textContent).toBe('Edit');
    await act(async () => document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', keyCode: 40, which: 40, bubbles: true, cancelable: true })));
    await advanceFrames();
    expect(document.activeElement?.textContent).toBe('Remove');
    await act(async () => document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true })));
    expect(execute).toHaveBeenCalledOnce();
    expect(execute.mock.calls[0][0].key).toBe('remove');
    expect(document.activeElement).toBe(target);
    await act(async () => target.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true })));
    await advanceFrames();
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })));
    expect(target.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(target);
  } finally { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); }
});

it('cancels obsolete focus during reopen and unmount while retaining the row focus target', async () => {
  const advanceFrames = controlAnimationFrames();
  const target = document.createElement('button'); target.textContent = 'Row'; document.body.append(target);
  function Harness() {
    const [open, setOpen] = useState(false);
    return <ContextActionMenu open={open} onOpenChange={setOpen} keyboardLabel="Row actions"
      point={{ x: 0, y: 0, focusTarget: target }} menu={{ items: [{ key: 'edit', label: 'Edit' }] }}><span>Row</span></ContextActionMenu>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  let mounted = true;
  try {
    await act(async () => root.render(<Harness />));
    const anchor = container.querySelector<HTMLElement>('[aria-label="Row actions"]')!;
    target.focus();
    await act(async () => anchor.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true })));
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })));
    await act(async () => anchor.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true })));
    await advanceFrames();
    expect(document.activeElement?.textContent).toBe('Edit');
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })));
    await advanceFrames();
    expect(document.activeElement).toBe(target);
    await act(async () => anchor.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true })));
    await act(async () => root.unmount()); mounted = false;
    target.focus();
    await advanceFrames();
    expect(document.activeElement).toBe(target);
    expect(document.querySelector('[role="menu"]')).toBeNull();
  } finally { if (mounted) await act(async () => root.unmount()); container.remove(); target.remove(); vi.unstubAllGlobals(); }
});

it('contains disabled, enabled and stale portal clicks without selecting the owning row', async () => {
  const select = vi.fn(); const execute = vi.fn();
  function Harness() {
    const [open, setOpen] = useState(false);
    return <div onClick={select}><ContextActionMenu open={open} onOpenChange={setOpen} keyboardLabel="Object actions"
      trigger={['contextMenu']} menu={{ items: [{ key: 'disabled', label: 'Unavailable', disabled: true }, { key: 'edit', label: 'Edit' }], onClick: execute }}><span>Object</span></ContextActionMenu></div>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<Harness />));
    await act(async () => container.querySelector<HTMLElement>('[aria-label="Object actions"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true })));
    const disabled = [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === 'Unavailable')!;
    await act(async () => disabled.click()); expect(select).not.toHaveBeenCalled(); expect(execute).not.toHaveBeenCalled();
    await act(async () => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === 'Edit')!.click());
    expect(execute).toHaveBeenCalledOnce(); expect(select).not.toHaveBeenCalled();
    await act(async () => disabled.click()); expect(select).not.toHaveBeenCalled(); expect(execute).toHaveBeenCalledOnce();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it.each(['input', 'textarea', 'a', 'contenteditable'])('preserves the native menu within %s', async kind => {
  function Harness() {
    const [open, setOpen] = useState(false);
    return <ContextActionMenu open={open} onOpenChange={setOpen} trigger={['contextMenu']}
      menu={{ items: [{ key: 'edit', label: 'Edit' }] }}>
      {kind === 'input' ? <input /> : kind === 'textarea' ? <textarea /> : kind === 'a' ? <a href="/object">Object</a> : <div contentEditable />}
    </ContextActionMenu>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<Harness />));
    const target = container.querySelector(kind === 'contenteditable' ? '[contenteditable]' : kind)!;
    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    await act(async () => target.dispatchEvent(event));
    expect(event.defaultPrevented).toBe(false);
    expect(document.querySelector('[role="menu"]')).toBeNull();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('does not impose keyboard focus when reopening a pointer context menu after Escape', async () => {
  const advanceFrames = controlAnimationFrames();
  const target = document.createElement('button'); document.body.append(target);
  function Harness() {
    const [open, setOpen] = useState(false);
    return <><button onClick={() => setOpen(true)}>Open pointer menu</button><ContextActionMenu open={open} onOpenChange={setOpen}
      point={{ x: 10, y: 20, focusTarget: target, keyboard: false }}
      menu={{ items: [{ key: 'edit', label: 'Edit' }] }}><span /></ContextActionMenu></>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<Harness />));
    for (let count = 0; count < 2; count++) {
      target.focus();
      await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
      await advanceFrames();
      expect(document.activeElement).toBe(target);
      target.blur();
      await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })));
      await advanceFrames();
      expect(document.activeElement).not.toBe(target);
    }
  } finally { await act(async () => root.unmount()); container.remove(); target.remove(); vi.unstubAllGlobals(); }
});
