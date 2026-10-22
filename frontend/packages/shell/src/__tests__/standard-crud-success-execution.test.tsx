// @vitest-environment jsdom
import { act } from 'react';
import { LocalizationProvider } from '@icp-platform/core';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ResourceChange, StandardCrudOperationResult } from '@icp-platform/sdk';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';
import { useStandardCrudMutationExecution } from '../standard-crud/useStandardCrudMutationExecution';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe.each(['action', 'mutation'] as const)('%s success execution wiring', (kind) => {
  let root: Root | undefined;
  afterEach(async () => { await act(async () => root?.unmount()); });

  async function renderExecution(options: {
    write: () => Promise<StandardCrudOperationResult>;
    onResult?: (result: StandardCrudOperationResult) => void;
    onCommitted?: (change: ResourceChange) => Promise<void>;
    refresh: () => void;
  }) {
    let current!: {
      execute: () => Promise<unknown>;
      pending: boolean;
      error: unknown;
      synchronizationError: string | null;
    };
    function ActionHarness() {
      const execution = useStandardCrudActionExecution({ resourceKey: 'directory', ...options });
      current = {
        ...execution, pending: execution.pendingKey !== null,
        execute: () => execution.execute({ key: 'save', handler: options.write, context: {} }),
      };
      return null;
    }
    function MutationHarness() {
      const execution = useStandardCrudMutationExecution({
        ...options, registration: { resourceKey: 'directory', dataAdapter: { create: options.write } },
      });
      current = {
        ...execution,
        execute: () => execution.execute({ mode: 'create', values: {}, detailFields: {}, fields: [] }),
      };
      return null;
    }
    root = createRoot(document.createElement('div'));
    await act(async () => root?.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={{ zhs: {} }}>{kind === 'action' ? <ActionHarness /> : <MutationHarness />}</LocalizationProvider>));
    return () => current;
  }

  const result: StandardCrudOperationResult = {
    operation: 'create', payload: { receipt: 'opaque' }, change: { operation: 'create', id: 'd' },
  };
  const succeeded = kind === 'action' ? { status: 'succeeded', result } : true;

  it.each([true, false])('keeps success and the last owner-formatted warning after follow-up failures (committed=%s)', async (hasCommitted) => {
    const write = vi.fn().mockResolvedValue(result);
    const onResult = vi.fn(() => { throw new Error('first'); });
    const onCommitted = vi.fn().mockRejectedValue(new Error('last'));
    const refresh = vi.fn(() => { throw new Error('last'); });
    const execution = await renderExecution({
      write, onResult, onCommitted: hasCommitted ? onCommitted : undefined, refresh,
    });
    let outcome: unknown;
    await act(async () => { outcome = await execution().execute(); });
    expect(outcome).toEqual(succeeded);
    expect(write).toHaveBeenCalledTimes(1);
    expect(onResult).toHaveBeenCalledTimes(1);
    expect(onResult.mock.calls[0][0]).toBe(result);
    expect(onCommitted).toHaveBeenCalledTimes(hasCommitted ? 1 : 0);
    expect(refresh).toHaveBeenCalledTimes(hasCommitted ? 0 : 1);
    expect(execution().synchronizationError).toBe('已保存，刷新失败。请重新加载。');
    expect(execution().error).toBeNull();
    expect(execution().pending).toBe(false);
  });

  it('holds the execution mutex until the committed handoff settles without replay or fallback', async () => {
    let release!: () => void;
    const onCommitted = vi.fn(() => new Promise<void>(resolve => { release = resolve; }));
    const write = vi.fn().mockResolvedValue(result);
    const refresh = vi.fn();
    const execution = await renderExecution({ write, onCommitted, refresh });
    let running!: Promise<unknown>;
    await act(async () => { running = execution().execute(); });
    expect(onCommitted).toHaveBeenCalledTimes(1);
    expect(execution().pending).toBe(true);
    let duplicate: unknown;
    await act(async () => { duplicate = await execution().execute(); });
    expect(duplicate).toEqual(kind === 'action' ? { status: 'ignored' } : false);
    let outcome: unknown;
    await act(async () => { release(); outcome = await running; });
    expect(outcome).toEqual(succeeded);
    expect(execution().pending).toBe(false);
    expect(write).toHaveBeenCalledTimes(1);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('releases pending without waiting for promises returned by void callbacks', async () => {
    const unfinished = new Promise<void>(() => {});
    const write = vi.fn().mockResolvedValue(result);
    const refresh = vi.fn(() => unfinished);
    const execution = await renderExecution({ write, onResult: () => unfinished, refresh });
    let outcome: unknown;
    await act(async () => { outcome = await execution().execute(); });
    expect(outcome).toEqual(succeeded);
    expect(execution().pending).toBe(false);
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});
