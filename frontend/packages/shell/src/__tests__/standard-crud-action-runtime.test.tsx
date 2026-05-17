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
    document.body.innerHTML = '';
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

  async function clickActionButton(label: string) {
    const button = Array.from(container.querySelectorAll('button')).find(
      (item) => item.textContent?.trim() === label,
    );

    expect(button).toBeDefined();

    await act(async () => {
      button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
  }

  async function clickPopconfirmButton(label: '确认' | '取消') {
    const button = Array.from(document.body.querySelectorAll('button')).find(
      (item) => item.textContent?.replace(/\s+/g, '') === label,
    );

    expect(button).toBeDefined();

    await act(async () => {
      button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
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

  it('opens Popconfirm for danger action and only runs handler after confirm', async () => {
    const handler = vi.fn();
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderActions({
      actions: [{ key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用？' }],
      handlers: { disable: handler },
    });

    await clickActionButton('停用');

    expect(document.body.textContent).toContain('确认停用？');
    expect(handler).not.toHaveBeenCalled();
    expect(confirmSpy).not.toHaveBeenCalled();

    await clickPopconfirmButton('确认');

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith({ id: 'Person_1', refresh: expect.any(Function) });
  });

  it('uses Popconfirm for delete standard action even without danger variant', async () => {
    const handler = vi.fn();
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderActions({
      actions: [{ key: 'delete', labelKey: '删除', standardAction: 'delete', confirmMessage: '确认删除？' }],
      handlers: { delete: handler },
    });

    await clickActionButton('删除');

    expect(document.body.textContent).toContain('确认删除？');
    expect(handler).not.toHaveBeenCalled();

    await clickPopconfirmButton('确认');

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith({ id: 'Person_1', refresh: expect.any(Function) });
  });

  it('does not run danger handler when Popconfirm is canceled', async () => {
    const handler = vi.fn();
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderActions({
      actions: [{ key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用？' }],
      handlers: { disable: handler },
    });

    await clickActionButton('停用');
    await clickPopconfirmButton('取消');

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(handler).not.toHaveBeenCalled();
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

  it('renders handler errors without throwing after Popconfirm confirm', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderActions({
      actions: [{ key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用？' }],
      handlers: {
        disable: vi.fn(async () => {
          throw new Error('停用失败');
        }),
      },
    });

    await clickActionButton('停用');
    await clickPopconfirmButton('确认');

    expect(confirmSpy).not.toHaveBeenCalled();
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
