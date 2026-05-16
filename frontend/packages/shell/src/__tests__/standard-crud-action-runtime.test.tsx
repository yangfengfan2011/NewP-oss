// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import type { StandardCrudActionDefinition, StandardCrudActionHandler } from '@icp-platform/sdk';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('standard CRUD action runtime', () => {
  let container: HTMLDivElement;
  let root: Root | null = null;

  afterEach(async () => {
    vi.restoreAllMocks();
    if (root) {
      await act(async () => root?.unmount());
      root = null;
    }
    container?.remove();
  });

  async function renderActions({
    actions,
    grantedPermissions = [],
    handlers,
  }: {
    actions: StandardCrudActionDefinition[];
    grantedPermissions?: string[];
    handlers?: Record<string, StandardCrudActionHandler>;
  }) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <StandardCrudActions
          actions={actions}
          grantedPermissions={grantedPermissions}
          handlers={handlers}
          context={{ id: 'Person_1', refresh: vi.fn() }}
        />,
      );
    });
  }

  it('filters actions by required permissions', async () => {
    await renderActions({
      actions: [
        { key: 'edit', labelKey: '编辑', requiredPermissions: ['Organization.Person.Edit'] },
        { key: 'disable', labelKey: '停用', requiredPermissions: ['Organization.Person.Disable'] },
      ],
      grantedPermissions: ['Organization.Person.Edit'],
      handlers: {
        edit: vi.fn(),
        disable: vi.fn(),
      },
    });

    expect(container.textContent).toContain('编辑');
    expect(container.textContent).not.toContain('停用');
  });

  it('runs handler after confirm', async () => {
    const handler = vi.fn();
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderActions({
      actions: [{ key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用？' }],
      handlers: { disable: handler },
    });

    await act(async () => {
      container.querySelector('button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(window.confirm).toHaveBeenCalledWith('确认停用？');
    expect(handler).toHaveBeenCalledWith({ id: 'Person_1', refresh: expect.any(Function) });
  });

  it('shows pending text and disables buttons while handler is running', async () => {
    let resolveHandler: (() => void) | undefined;
    const handler = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveHandler = resolve;
        }),
    );

    await renderActions({
      actions: [
        { key: 'disable', labelKey: '停用' },
        { key: 'edit', labelKey: '编辑' },
      ],
      handlers: {
        disable: handler,
        edit: vi.fn(),
      },
    });

    await act(async () => {
      container.querySelector('button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    const buttons = Array.from(container.querySelectorAll('button'));
    expect(buttons[0].textContent).toBe('处理中...');
    expect(buttons.every((button) => button.disabled)).toBe(true);

    await act(async () => {
      resolveHandler?.();
    });
  });

  it('renders handler errors without throwing', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderActions({
      actions: [{ key: 'disable', labelKey: '停用', confirmMessage: '确认停用？' }],
      handlers: {
        disable: vi.fn(async () => {
          throw new Error('停用失败');
        }),
      },
    });

    await act(async () => {
      container.querySelector('button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(container.textContent).toContain('停用失败');
  });

  it('disables actions without a handler', async () => {
    await renderActions({
      actions: [{ key: 'archive', labelKey: '归档' }],
      handlers: {},
    });

    expect(container.querySelector('button')?.disabled).toBe(true);
  });
});
