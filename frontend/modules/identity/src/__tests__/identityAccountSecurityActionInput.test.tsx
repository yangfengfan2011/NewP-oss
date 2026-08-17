// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  identityAccountDeleteConfirmationInput,
  identityAccountSecurityReasonInput,
} from '../standard-crud/IdentityAccountSecurityActionInput';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('Identity account security action input', () => {
  let root: Root | null = null;
  let container: HTMLDivElement;

  beforeEach(() => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    });
  });

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    root = null;
    container?.remove();
    document.body.innerHTML = '';
  });

  async function renderInput(kind: 'reason' | 'delete', onChange = vi.fn()) {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    const renderer = kind === 'delete' ? identityAccountDeleteConfirmationInput : identityAccountSecurityReasonInput;
    await act(async () => root?.render(renderer({
      action: { key: kind, labelKey: kind },
      value: { reason: '', confirmation: '' },
      onChange,
      row: {
        EID: 'User_2', ECODE: 'alice', ENAME: 'Alice', Status: 'Disabled', Revision: 7,
      },
    })));
    return onChange;
  }

  it('shows the current security snapshot and collects a required operation reason with Ant Design controls', async () => {
    const onChange = await renderInput('reason');

    expect(document.body.textContent).toContain('alice');
    expect(document.body.textContent).toContain('Alice');
    expect(document.body.textContent).toContain('Disabled');
    const reason = document.body.querySelector<HTMLTextAreaElement>('textarea[aria-label="操作原因"]');
    expect(reason?.classList.contains('ant-input')).toBe(true);
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set?.call(reason, '风险处置');
      reason?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    expect(onChange).toHaveBeenCalledWith({ reason: '风险处置', confirmation: '' });
  });

  it('adds an exact full ECODE confirmation only for irreversible deletion', async () => {
    const onChange = await renderInput('delete');
    expect(document.body.textContent).toContain('不可恢复');
    const confirmation = document.body.querySelector<HTMLInputElement>('input[aria-label="完整登录账号确认"]');
    expect(confirmation?.classList.contains('ant-input')).toBe(true);
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(confirmation, 'alice');
      confirmation?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    expect(onChange).toHaveBeenCalledWith({ reason: '', confirmation: 'alice' });
  });
});
