// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { ResizableHeaderCell } from '../../../ui/src/pro-adapters/ResizableHeaderCell';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
it('previews a drag, commits once, cancels interrupted drags and does not sort the column', async () => {
  const change = vi.fn(); const sort = vi.fn();
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<table><thead><tr><ResizableHeaderCell resizeLabel="Name resize" resizeWidth={200} onResizeWidth={change} onClick={sort}>Name</ResizableHeaderCell></tr></thead></table>));
    const cell = container.querySelector('th')!;
    vi.spyOn(cell, 'getBoundingClientRect').mockReturnValue({ width: 200 } as DOMRect);
    const handle = container.querySelector<HTMLElement>('[role=separator]')!;
    const pointer = async (type: string, x: number) => {
      const event = new MouseEvent(type, { clientX: x, button: 0, bubbles: true }); Object.defineProperty(event, 'pointerId', { value: 1 });
      await act(async () => handle.dispatchEvent(event));
    };
    await pointer('pointerdown', 100); await pointer('pointermove', 160);
    expect(change).toHaveBeenLastCalledWith(260, false);
    await pointer('pointerup', 160);
    expect(change.mock.calls.filter(([, commit]) => commit)).toEqual([[260, true]]);
    await act(async () => handle.click()); expect(sort).not.toHaveBeenCalled();
    await pointer('pointerdown', 100); await pointer('pointermove', 140); await pointer('pointercancel', 140);
    expect(change).toHaveBeenLastCalledWith(undefined, false);
    await act(async () => handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })));
    expect(change).toHaveBeenLastCalledWith(210, true);
  } finally { await act(async () => root.unmount()); container.remove(); }
});
