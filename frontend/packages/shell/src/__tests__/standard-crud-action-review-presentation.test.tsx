// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';

it('honors reusable input width and closes read-only details without executing a handler', async () => {
  const input = Object.assign(() => <span>权限来源详情</span>, { dialogWidth: 960, readOnly: true });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const close = vi.fn(); const confirm = vi.fn();
  const props = { action: { key: 'inspect', labelKey: '查看授权' }, title: '查看授权', inputRenderer: input,
    value: undefined, pending: false, onChange: vi.fn(), onCancel: close, onConfirm: confirm };
  try {
    await act(async () => root.render(<StandardCrudActionReviewDialog {...props} />));
    expect((document.querySelector('.ant-modal') as HTMLElement).style.width).toBe('960px');
    expect(document.querySelector('.ant-modal-footer')?.textContent?.replace(/\s/g, '')).toBe('关闭');
    await act(async () => (document.querySelector('.ant-modal-footer button') as HTMLElement).click());
    expect(close).toHaveBeenCalledOnce(); expect(confirm).not.toHaveBeenCalled();
    await act(async () => root.render(<StandardCrudActionReviewDialog {...props} inputRenderer={() => <span>可编辑输入</span>} />));
    expect((document.querySelector('.ant-modal') as HTMLElement).style.width).toBe('520px');
    expect(document.querySelector('.ant-modal-footer')?.textContent?.replace(/\s/g, '')).toBe('取消确认');
  } finally { await act(async () => root.unmount()); container.remove(); }
});
