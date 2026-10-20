// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import type { Root } from 'react-dom/client';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { createStandardCrudClient, StandardCrudRequestError } from '@icp-platform/sdk';
import { bindActionDataAccess } from '../metadata/runtimeMetadataLoader';
import type {
  StandardCrudActionDefinition,
  StandardCrudActionHandler,
  StandardCrudActionInputRenderer,
  StandardCrudActionPrecheck,
  StandardCrudActionStateResolver,
  StandardCrudOperationResult,
} from '@icp-platform/sdk';
import type { FeatureResolution } from '../authorization/featurePlacementResolver';

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
    grantedPositions = actions.map(action => action.key),
    handlers,
    actionInputs,
    actionStateResolver,
    actionPrechecks,
    onResult,
    refresh = vi.fn(),
  }: {
    actions: StandardCrudActionDefinition[];
    grantedPositions?: string[];
    handlers?: Record<string, StandardCrudActionHandler>;
    actionInputs?: Record<string, StandardCrudActionInputRenderer>;
    actionStateResolver?: StandardCrudActionStateResolver;
    actionPrechecks?: Record<string, StandardCrudActionPrecheck>;
    onResult?: (result: StandardCrudOperationResult) => void;
    refresh?: () => void;
  }) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <StandardCrudActions
          actions={actions}
          featureResolutionForAction={action => grantedPositions.includes(action.key)
            ? { kind: 'allowed', position: action.key }
            : { kind: 'denied', position: action.key }}
          handlers={handlers}
          actionInputs={actionInputs}
          actionStateResolver={actionStateResolver}
          actionPrechecks={actionPrechecks}
          onResult={onResult}
          refresh={refresh}
          context={{ id: 'Person_1', detail: { revision: 4 } }}
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

  it('filters actions by their declared feature positions', async () => {
    await renderActions({
      actions: [
        { key: 'edit', labelKey: '编辑' },
        { key: 'disable', labelKey: '停用' },
      ],
      grantedPositions: ['edit'],
      handlers: {
        edit: vi.fn(),
        disable: vi.fn(),
      },
    });

    expect(container.textContent).toContain('编辑');
    expect(container.textContent).not.toContain('停用');
  });

  it.each([true, false])('passes the formal button identity and feature from the clicked control to transport (dataAccess=%s)', async declared => {
    const id = 'Test/view:items/rowActions/action:edit';
    const definition = bindActionDataAccess({ view: { key: 'items', rowActions: [{ key: 'edit', labelKey: '编辑', handlerKey: 'save',
      ...(declared ? { dataAccess: { object: 'Person', actions: ['edit'] } } : {}) }] } }, [{ id, moduleId: 'Test', viewKey: 'items',
      surface: 'rowActions', actionKey: 'edit', execution: '', resources: ['test.person'], actions: declared ? ['edit'] : [],
      featurePositions: ['edit'], businessBinding: 'test.person.save', ...(declared ? { object: 'Person' } : {}) }]);
    const fetcher = vi.fn(async () => new Response(JSON.stringify({ fields: { EID: 'Person_1' } })));
    const client = createStandardCrudClient('/api/test/persons', fetcher);
    await renderActions({ actions: definition.view.rowActions as StandardCrudActionDefinition[],
      actionPrechecks: { edit: async context => { expect(context.buttonId).toBe(id); } },
      handlers: { save: async context => { await client.update(context.id!, { ENAME: '已编辑' }, context); } } });
    await clickActionButton('编辑');
    expect(fetcher).toHaveBeenCalledTimes(1);
    const headers = new Headers((fetcher.mock.calls[0] as unknown as [string, RequestInit])[1].headers);
    expect(headers.get('X-NewP-Button')).toBe(id);
    expect(headers.get('X-NewP-Feature')).toBe('edit');
    expect(headers.has('X-NewP-Data-Declaration')).toBe(false);
  });

  it('carries the compiled heterogeneous object requirements from the clicked button without separate permission inputs', async () => {
    const id = 'Test/view:items/rowActions/action:history';
    const objectRequirements = [{ object: 'Part', actions: ['read'], resourceKey: 'part.part' },
      { object: 'Document', actions: ['read'], resourceKey: 'document.document' }];
    const definition = bindActionDataAccess({ view: { key: 'items', rowActions: [{ key: 'history', labelKey: '履历', handlerKey: 'history',
      dataAccess: objectRequirements.map(({ object, actions }) => ({ object, actions })) }] } }, [{ id, moduleId: 'Test', viewKey: 'items',
      surface: 'rowActions', actionKey: 'history', execution: '', resources: ['part.part', 'document.document'], actions: [],
      featurePositions: ['history'], businessBinding: 'test.history', objectRequirements }]);
    const handler = vi.fn();
    await renderActions({ actions: definition.view.rowActions as StandardCrudActionDefinition[], grantedPositions: ['history'], handlers: { history: handler } });
    await clickActionButton('履历');
    expect(handler).toHaveBeenCalledOnce();
    expect(handler.mock.calls[0][0]).toMatchObject({ buttonId: id, action: { dataAccess: { objectRequirements } } });
  });

  it.each([true, false])('checks a declared handler entry before executing it (allowed=%s)', async allowed => {
    const handler = vi.fn();
    const precheck = vi.fn(async () => { if (!allowed) throw new Error('Entry denied'); });
    await renderActions({
      actions: [{ key: 'inspect', labelKey: '检查', dataAccess: {
        id: 'Test/view:items/rowActions/action:inspect', moduleId: 'Test', viewKey: 'items', surface: 'rowActions',
        actionKey: 'inspect', execution: 'test.inspect', resources: ['test.item'], actions: ['read'], featurePositions: ['inspect'],
      } }],
      handlers: { inspect: handler }, actionPrechecks: { inspect: precheck },
    });
    await clickActionButton('检查');
    expect(precheck).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledTimes(allowed ? 1 : 0);
    if (!allowed) expect(container.querySelector('[role="alert"]')?.textContent).toContain('操作失败，请稍后重试。');
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
    expect(handler).toHaveBeenCalledWith(expect.objectContaining({ id: 'Person_1' }));
    expect(handler.mock.calls[0]?.[0]).not.toHaveProperty('refresh');
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
    expect(handler).toHaveBeenCalledWith(expect.objectContaining({ id: 'Person_1' }));
    expect(handler.mock.calls[0]?.[0]).not.toHaveProperty('refresh');
  });

  it('does not refresh a failed delete and refreshes one successful delete exactly once', async () => {
    const refresh = vi.fn();
    const handler = vi.fn()
      .mockRejectedValueOnce(new StandardCrudRequestError('岗位当前被人员引用，不能删除。', 409))
      .mockResolvedValueOnce(undefined);

    await renderActions({
      actions: [{ key: 'delete', labelKey: '删除', standardAction: 'delete', confirmMessage: '确认删除？' }],
      handlers: { delete: handler },
      refresh,
    });

    await clickActionButton('删除');
    await clickPopconfirmButton('确认');
    await act(async () => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(handler).toHaveBeenCalledTimes(1);
    expect(refresh).not.toHaveBeenCalled();
    expect(container.textContent).toContain('记录已发生变化，未覆盖服务器上的最新数据。请重新加载后再编辑。');

    await clickActionButton('删除');
    await clickPopconfirmButton('确认');
    await act(async () => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(handler).toHaveBeenCalledTimes(2);
    expect(refresh).toHaveBeenCalledTimes(1);
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

  it('executes a remote action only once when it is triggered repeatedly before pending renders', async () => {
    let resolveHandler: (() => void) | undefined;
    const handler = vi.fn(() => new Promise<void>((resolve) => {
      resolveHandler = resolve;
    }));

    await renderActions({
      actions: [{ key: 'archive', labelKey: '归档' }],
      handlers: { archive: handler },
    });

    const button = container.querySelector('button');
    await act(async () => {
      button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(handler).toHaveBeenCalledTimes(1);

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
    expect(container.textContent).toContain('操作失败，请稍后重试。');
  });

  it('disables actions without a handler', async () => {
    await renderActions({
      actions: [{ key: 'archive', labelKey: '归档' }],
      handlers: {},
    });

    expect(container.querySelector('button')?.disabled).toBe(true);
  });

  it('runs a registered controlled input action through the shared dialog lifecycle', async () => {
    const handler = vi.fn();

    await renderActions({
      actions: [{ key: 'archive', labelKey: '归档', inputKey: 'archive-input' }],
      handlers: { archive: handler },
      actionInputs: {
        'archive-input': ({ value, onChange, detail }) => (
          <>
            <span>快照 {String((detail as { revision?: number })?.revision)}</span>
            <input aria-label="归档说明" value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} />
          </>
        ),
      },
    });

    await clickActionButton('归档');
    expect(document.body.textContent).toContain('快照 4');
    const input = document.body.querySelector('input[aria-label="归档说明"]') as HTMLInputElement;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(input, '季度归档');
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await clickPopconfirmButton('确认');
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(handler).toHaveBeenCalledWith(expect.objectContaining({ input: '季度归档' }));
    expect(document.body.querySelector('input[aria-label="归档说明"]')).toBeNull();
  });

  it('does not run an input action after the current rendered decision is revoked', async () => {
    const handler = vi.fn();
    const action = { key: 'archive', labelKey: '归档', inputKey: 'archive-input' };
    let resolution: FeatureResolution = { kind: 'allowed', position: 'archive-position' };
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    const render = async () => act(async () => root?.render(
      <StandardCrudActions
        actions={[action]}
        featureResolutionForAction={() => resolution}
        handlers={{ archive: handler }}
        actionInputs={{ 'archive-input': () => <span>归档输入</span> }}
        refresh={vi.fn()}
        context={{ id: 'Person_1' }}
      />,
    ));

    await render();
    await clickActionButton('归档');
    expect(document.body.textContent).toContain('归档输入');

    resolution = { kind: 'denied', position: 'archive-position' };
    await render();
    await clickPopconfirmButton('确认');

    expect(handler).not.toHaveBeenCalled();
  });

  it('uses one review dialog for a danger action that also requires input', async () => {
    const handler = vi.fn();

    await renderActions({
      actions: [{
        key: 'disable',
        labelKey: '停用',
        variant: 'danger',
        confirmMessage: '确认停用当前记录？',
        inputKey: 'reason',
      }],
      handlers: { disable: handler },
      actionInputs: {
        reason: ({ value, onChange }) => (
          <input aria-label="停用原因" value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} />
        ),
      },
    });

    await clickActionButton('停用');

    expect(document.body.textContent).toContain('确认停用当前记录？');
    expect(document.body.querySelector('input[aria-label="停用原因"]')).not.toBeNull();
    expect(handler).not.toHaveBeenCalled();
  });

  it('applies module action state without exposing business fields in the shared contract', async () => {
    const resolver: StandardCrudActionStateResolver = vi.fn(({ action, detail }) => ({
      visible: action.key !== 'hidden',
      available: action.key !== 'blocked',
      reason: action.key === 'blocked' ? `最新快照 ${String((detail as { revision?: number })?.revision)}` : undefined,
    }));

    await renderActions({
      actions: [
        { key: 'available', labelKey: '可执行' },
        { key: 'blocked', labelKey: '已阻止' },
        { key: 'hidden', labelKey: '不显示' },
      ],
      handlers: {
        available: vi.fn(),
        blocked: vi.fn(),
        hidden: vi.fn(),
      },
      actionStateResolver: resolver,
    });

    expect(container.textContent).toContain('可执行');
    expect(container.textContent).toContain('已阻止');
    expect(container.textContent).toContain('最新快照 4');
    expect(container.textContent).not.toContain('不显示');
    const blocked = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '已阻止');
    expect(blocked?.disabled).toBe(true);
  });

  it('hands an opaque action result to the shared result callback', async () => {
    const result: StandardCrudOperationResult = { operation: 'action', payload: { receipt: 'opaque' } };
    const onResult = vi.fn();
    const refresh = vi.fn();

    await renderActions({
      actions: [{ key: 'rotate', labelKey: '轮换' }],
      handlers: { rotate: vi.fn().mockResolvedValue(result) },
      onResult,
      refresh,
    });
    await clickActionButton('轮换');

    expect(onResult).toHaveBeenCalledWith(result);
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('refreshes and closes stale confirmation on 409 without replaying the action', async () => {
    const refresh = vi.fn();
    const handler = vi.fn().mockRejectedValue(new StandardCrudRequestError(
      '最新状态 Disabled，Revision 8，请重新确认。',
      409,
    ));

    await renderActions({
      actions: [{ key: 'secure', labelKey: '安全操作', inputKey: 'reason' }],
      handlers: { secure: handler },
      actionInputs: {
        reason: ({ value, onChange }) => (
          <input aria-label="原因" value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} />
        ),
      },
      refresh,
    });
    await clickActionButton('安全操作');
    await clickPopconfirmButton('确认');
    await act(async () => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(handler).toHaveBeenCalledTimes(1);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(document.body.querySelector('input[aria-label="原因"]')).toBeNull();
    expect(container.textContent).toContain('记录已发生变化，未覆盖服务器上的最新数据。请重新加载后再编辑。');
  });

  it.each([true, false])('only retains input for a 409 explicitly classified by its handler (correctable=%s)', async correctable => {
    const refresh = vi.fn();
    const failure = new StandardCrudRequestError('输入冲突', 409, { Code: '重复代码' }, { code: 'Example.CodeConflict' });
    const classify = vi.fn((error: unknown) => correctable && error === failure);
    const handler = Object.assign(vi.fn().mockRejectedValue(failure), { isCorrectableInputError: classify });
    await renderActions({
      actions: [{ key: 'secure', labelKey: '安全操作', inputKey: 'reason' }], handlers: { secure: handler },
      actionInputs: { reason: () => <input aria-label="原因" defaultValue="保留草稿" /> }, refresh,
    });
    await clickActionButton('安全操作');
    await clickPopconfirmButton('确认');
    await act(async () => undefined);
    expect(handler).toHaveBeenCalledTimes(1);
    expect(classify).toHaveBeenCalledWith(failure);
    expect(failure.status).toBe(409);
    expect(failure.fieldErrors).toEqual({ Code: '重复代码' });
    expect(document.body.querySelector('input[aria-label="原因"]') !== null).toBe(correctable);
    expect(refresh).toHaveBeenCalledTimes(correctable ? 0 : 1);
  });
});
