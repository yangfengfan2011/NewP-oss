// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it } from 'vitest';
import { ContextActionMenu } from '@icp-platform/ui/core';

it('opens with the keyboard, closes with Escape, and restores focus to the same target', async () => {
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
    expect(target.getAttribute('aria-expanded')).toBe('true');
    expect(document.querySelector('[role="menuitem"]')?.textContent).toBe('Edit');
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })));
    expect(target.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(target);
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
