// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, StrictMode } from 'react';
import type { Root } from 'react-dom/client';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { PlatformQueryProvider } from '@icp-platform/core';
import type { ActionDataAccessRequirement, StandardCrudLoadedDetail } from '@icp-platform/sdk';
import { useStandardCrudDetailQuery, type StandardCrudDetailQueryResult } from '../standard-crud/standardCrudDetailQuery';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('Standard CRUD detail query', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) await act(async () => root.unmount());
    container?.remove();
    vi.restoreAllMocks();
  });

  it('isolates declarations at the same resource, target and feature position and cancels a pending reload', async () => {
    const original = { normalized: { fields: { EID: 'record-1', ENAME: 'read' }, summaries: {} }, value: {} };
    const prepared = { normalized: { fields: { EID: 'record-1', ENAME: 'prepare' }, summaries: {} }, value: {} };
    let finishOld!: (value: typeof original) => void;
    const get = vi.fn().mockResolvedValueOnce(original).mockImplementationOnce(() => new Promise(resolve => { finishOld = resolve; })).mockResolvedValueOnce(prepared);
    let current!: StandardCrudDetailQueryResult<unknown>;
    function Subscriber({ declarationId }: { declarationId: string }) {
      const dataAccess: ActionDataAccessRequirement = { id: declarationId, moduleId: 'Example', viewKey: 'records', surface: 'read', actionKey: '',
        execution: 'example.record.read', resources: ['example.record'], actions: ['read'], featurePositions: ['entry'] };
      current = useStandardCrudDetailQuery({ resourceKey: 'example.record', id: 'record-1', featurePosition: 'entry', dataAccess,
        dataAdapter: { get, list: vi.fn(), getRowId: () => 'record-1' } });
      return null;
    }
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
    await act(async () => root.render(<PlatformQueryProvider scopeKey="actor"><Subscriber declarationId="read" /></PlatformQueryProvider>));
    let reload!: ReturnType<typeof current.reload>;
    await act(async () => { reload = current.reload(); });
    const oldSignal = get.mock.calls[1][1].signal as AbortSignal;
    await act(async () => root.render(<PlatformQueryProvider scopeKey="actor"><Subscriber declarationId="prepare" /></PlatformQueryProvider>));
    expect(await reload).toEqual({ status: 'cancelled' });
    await vi.waitFor(() => expect(oldSignal.aborted).toBe(true));
    expect(get.mock.calls[2][1]).toMatchObject({ dataAccess: { id: 'prepare' }, featurePosition: 'entry' });
    await act(async () => finishOld(original));
    expect(current.detail).toBe(prepared);
  });

  it('isolates the same action record by scope, aborts the old request, and ignores its late result', async () => {
    let finishOld!: (detail: StandardCrudLoadedDetail) => void;
    const old = new Promise<StandardCrudLoadedDetail>(resolve => { finishOld = resolve; });
    const latest = { normalized: { fields: { ENAME: 'new parent' }, summaries: {} }, value: {} };
    const get = vi.fn().mockReturnValueOnce(old).mockResolvedValueOnce(latest);
    let current!: StandardCrudDetailQueryResult<unknown>;
    const adapter = { get, list: vi.fn(), getRowId: () => 'same-link' };
    function Subscriber({ parent }: { parent: string }) {
      current = useStandardCrudDetailQuery({ resourceKey: 'workspace.folder', id: 'same-link', dataAdapter: adapter,
        featurePosition: 'tree-edit', queryScope: { linkIds: parent } });
      return null;
    }
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
    await act(async () => root.render(<PlatformQueryProvider scopeKey="actor"><Subscriber parent='["a"]' /></PlatformQueryProvider>));
    const oldSignal = get.mock.calls[0][1].signal as AbortSignal;
    await act(async () => root.render(<PlatformQueryProvider scopeKey="actor"><Subscriber parent='["b"]' /></PlatformQueryProvider>));
    // Core 以短暂宽限期兼容 StrictMode 重订阅；范围切换最终取消真正失去消费者的请求。
    await vi.waitFor(() => expect(oldSignal.aborted).toBe(true));
    expect(get).toHaveBeenCalledTimes(2);
    expect(get.mock.calls[1][1]).toMatchObject({ queryScope: { linkIds: '["b"]' }, featurePosition: 'tree-edit' });
    await act(async () => finishOld({ normalized: { fields: { ENAME: 'old parent' }, summaries: {} }, value: {} }));
    expect(current.detail).toBe(latest);
  });

  it('settles one explicit reload even when its loader returns the same detail object', async () => {
    const detail = { normalized: { fields: { EID: 'Person_1' }, summaries: {} }, value: {} };
    let complete!: () => void;
    const get = vi.fn().mockResolvedValueOnce(detail).mockImplementationOnce(() => new Promise(resolve => { complete = () => resolve(detail); }));
    let current!: StandardCrudDetailQueryResult<unknown>;
    function Subscriber() {
      current = useStandardCrudDetailQuery({ resourceKey: 'organization.person', id: 'Person_1', dataAdapter: {
        get, list: vi.fn(), getRowId: () => 'Person_1',
      } });
      return null;
    }
    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => { root = createRoot(container); root.render(<PlatformQueryProvider scopeKey="user-1"><Subscriber /></PlatformQueryProvider>); });
    let reload!: ReturnType<typeof current.reload>;
    await act(async () => {
      reload = current.reload();
      expect(current.reload()).toBe(reload);
    });
    expect(get).toHaveBeenCalledTimes(2);
    await act(async () => complete());
    expect(await reload).toEqual({ status: 'success', detail });
    expect(current.detail).toBe(detail);
  });

  it.each(['failure', 'scope', 'unmount'] as const)('settles an explicit reload on %s without adopting a later read', async ending => {
    const original = { normalized: { fields: { EID: 'Person_1' }, summaries: {} }, value: {} };
    const later = { normalized: { fields: { EID: 'Person_1', ENAME: 'later' }, summaries: {} }, value: {} };
    let resolve!: (detail: typeof original) => void;
    let reject!: (error: Error) => void;
    const get = vi.fn().mockResolvedValueOnce(original)
      .mockImplementationOnce(() => new Promise((res, rej) => { resolve = res; reject = rej; }))
      .mockResolvedValue(later);
    let current!: StandardCrudDetailQueryResult<unknown>;
    function Subscriber() {
      current = useStandardCrudDetailQuery({ resourceKey: 'organization.person', id: 'Person_1', dataAdapter: {
        get, list: vi.fn(), getRowId: () => 'Person_1',
      } });
      return null;
    }
    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => { root = createRoot(container); root.render(<PlatformQueryProvider scopeKey="zh"><Subscriber /></PlatformQueryProvider>); });
    let reload!: ReturnType<typeof current.reload>;
    await act(async () => { reload = current.reload(); });
    if (ending === 'failure') await act(async () => reject(new Error('read failed')));
    else await act(async () => root.render(ending === 'unmount' ? null : <PlatformQueryProvider scopeKey="en"><Subscriber /></PlatformQueryProvider>));
    expect(await reload).toEqual({ status: ending === 'failure' ? 'failed' : 'cancelled' });
    await act(async () => resolve(later));
    if (ending === 'failure') await act(async () => current.retry());
    if (ending !== 'unmount') expect(current.detail).toBe(later);
    expect(await reload).toEqual({ status: ending === 'failure' ? 'failed' : 'cancelled' });
  });

  it('shares one typed detail generation across StrictMode page and drawer subscribers', async () => {
    let resolveDetail!: (detail: StandardCrudLoadedDetail) => void;
    const pending = new Promise<StandardCrudLoadedDetail>((resolve) => {
      resolveDetail = resolve;
    });
    const get = vi.fn((_id: string, _context?: { signal?: AbortSignal }) => pending);
    const adapter = {
      list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
      get,
      getRowId: () => 'Person_1',
    };
    const observed: Array<StandardCrudLoadedDetail | undefined> = [];

    function DetailSubscriber({ slot }: { slot: number }) {
      const result = useStandardCrudDetailQuery({
        resourceKey: 'organization.person',
        id: 'Person_1',
        dataAdapter: adapter,
      });
      observed[slot] = result.detail;
      return <span>{result.detail ? `ready-${slot}` : result.loading ? `loading-${slot}` : `idle-${slot}`}</span>;
    }

    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(
        <StrictMode>
          <PlatformQueryProvider scopeKey="user-1">
            <DetailSubscriber slot={0} />
            <DetailSubscriber slot={1} />
          </PlatformQueryProvider>
        </StrictMode>,
      );
    });

    expect(get).toHaveBeenCalledTimes(1);
    expect(get.mock.calls[0][0]).toBe('Person_1');
    expect(get.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal);

    const loadedDetail = {
      normalized: { fields: { EID: 'Person_1', ENAME: '张三' }, summaries: {} },
      value: { id: 'Person_1', name: '张三' },
    };
    await act(async () => resolveDetail(loadedDetail));

    expect(container.textContent).toContain('ready-0');
    expect(container.textContent).toContain('ready-1');
    expect(observed[0]).toBe(loadedDetail);
    expect(observed[1]).toBe(observed[0]);
  });

  it('isolates a superseded id generation and aborts its orphaned request', async () => {
    const pendingById = new Map<string, {
      resolve: (detail: StandardCrudLoadedDetail) => void;
      signal: AbortSignal;
    }>();
    const get = vi.fn((id: string, context?: { signal?: AbortSignal }) => new Promise<StandardCrudLoadedDetail>((resolve) => {
      pendingById.set(id, { resolve, signal: context!.signal! });
    }));
    const adapter = {
      list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
      get,
      getRowId: () => 'unused',
    };

    function DetailSubscriber({ id }: { id: string }) {
      const result = useStandardCrudDetailQuery({
        resourceKey: 'organization.person',
        id,
        dataAdapter: adapter,
      });
      return <span>{result.detail?.normalized.fields.ENAME ?? 'loading'}</span>;
    }

    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(
        <PlatformQueryProvider scopeKey="user-1">
          <DetailSubscriber id="Person_A" />
        </PlatformQueryProvider>,
      );
    });
    expect(get).toHaveBeenCalledTimes(1);

    await act(async () => {
      root.render(
        <PlatformQueryProvider scopeKey="user-1">
          <DetailSubscriber id="Person_B" />
        </PlatformQueryProvider>,
      );
    });
    expect(get).toHaveBeenCalledTimes(2);

    const detailB = {
      normalized: { fields: { EID: 'Person_B', ENAME: '李四' }, summaries: {} },
      value: { id: 'Person_B', name: '李四' },
    };
    await act(async () => pendingById.get('Person_B')!.resolve(detailB));
    expect(container.textContent).toBe('李四');

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 60));
    });
    expect(pendingById.get('Person_A')!.signal.aborted).toBe(true);

    const detailA = {
      normalized: { fields: { EID: 'Person_A', ENAME: '张三' }, summaries: {} },
      value: { id: 'Person_A', name: '张三' },
    };
    await act(async () => pendingById.get('Person_A')!.resolve(detailA));
    expect(container.textContent).toBe('李四');
  });

  it('coalesces explicit retry and refresh while retaining successful detail during refresh', async () => {
    let resolveInitial!: (detail: StandardCrudLoadedDetail) => void;
    let resolveRefresh!: (detail: StandardCrudLoadedDetail) => void;
    const initial = new Promise<StandardCrudLoadedDetail>((resolve) => {
      resolveInitial = resolve;
    });
    const refresh = new Promise<StandardCrudLoadedDetail>((resolve) => {
      resolveRefresh = resolve;
    });
    const get = vi.fn()
      .mockRejectedValueOnce(new Error('详情读取失败'))
      .mockReturnValueOnce(initial)
      .mockReturnValueOnce(refresh);
    const adapter = {
      list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
      get,
      getRowId: () => 'Person_1',
    };

    function DetailSubscriber() {
      const result = useStandardCrudDetailQuery({
        resourceKey: 'organization.person',
        id: 'Person_1',
        dataAdapter: adapter,
      });
      return (
        <div>
          <span>{result.detail?.normalized.fields.ENAME ?? result.error ?? 'loading'}</span>
          <span>{result.refreshing ? 'refreshing' : 'settled'}</span>
          <button type="button" onClick={result.retry}>Retry</button>
          <button type="button" onClick={result.refresh}>Refresh</button>
        </div>
      );
    }

    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(
        <PlatformQueryProvider scopeKey="user-1">
          <DetailSubscriber />
        </PlatformQueryProvider>,
      );
    });
    expect(container.textContent).toContain('操作失败，请稍后重试。');

    const retryButton = Array.from(container.querySelectorAll('button'))
      .find((button) => button.textContent === 'Retry')!;
    await act(async () => {
      retryButton.click();
      retryButton.click();
    });
    expect(get).toHaveBeenCalledTimes(2);

    await act(async () => resolveInitial({
      normalized: { fields: { EID: 'Person_1', ENAME: '张三' }, summaries: {} },
      value: { id: 'Person_1', name: '张三' },
    }));
    expect(container.textContent).toContain('张三');

    const refreshButton = Array.from(container.querySelectorAll('button'))
      .find((button) => button.textContent === 'Refresh')!;
    await act(async () => {
      refreshButton.click();
      refreshButton.click();
    });
    expect(get).toHaveBeenCalledTimes(3);
    expect(container.textContent).toContain('张三');
    expect(container.textContent).toContain('refreshing');

    await act(async () => resolveRefresh({
      normalized: { fields: { EID: 'Person_1', ENAME: '张三（刷新）' }, summaries: {} },
      value: { id: 'Person_1', name: '张三（刷新）' },
    }));
    expect(container.textContent).toContain('张三（刷新）');
    expect(container.textContent).toContain('settled');
  });
});
