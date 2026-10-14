import { describe, expect, it, vi } from 'vitest';
import type { StandardCrudOperationResult } from '@icp-platform/sdk';
import { handoffStandardCrudSuccess } from '../standard-crud/standardCrudSuccessHandoff';

it('hands off a confirmed no-change receipt without publishing a change or rereading', async () => {
  const onResult = vi.fn(); const onCommitted = vi.fn(); const refresh = vi.fn();
  const result = { operation: 'action' as const, payload: { moved: false, boundary: 'first' },
    noChange: true as const, notice: { reasonKey: 'Workspace.TreeOrderFirst' } };
  await handoffStandardCrudSuccess({ owner: { kind: 'action', resourceKey: 'workspace.folder' }, result,
    onResult, onCommitted, refresh, onSynchronizationError: vi.fn() });
  expect(onResult).toHaveBeenCalledWith(result);
  expect(onCommitted).not.toHaveBeenCalled(); expect(refresh).not.toHaveBeenCalled();
});

it('reports a contradictory no-change flag while preserving the actual committed change', async () => {
  const onCommitted = vi.fn(); const refresh = vi.fn(); const report = vi.fn();
  await handoffStandardCrudSuccess({ owner: { kind: 'action', resourceKey: 'directory' },
    result: { operation: 'action', payload: {}, noChange: true, change: { operation: 'update', id: 'real' } },
    onCommitted, refresh, onSynchronizationError: report });
  expect(report).toHaveBeenCalledTimes(1);
  expect(onCommitted).toHaveBeenCalledWith({ resourceKey: 'directory', operation: 'update', id: 'real' });
  expect(refresh).not.toHaveBeenCalled();
});

it('registers the original result before publishing one owner-qualified change without fallback', async () => {
  const result: StandardCrudOperationResult = {
    operation: 'action', payload: { receipt: 'opaque' },
    change: { operation: 'delete', id: 'd', before: { parent: 'p' } },
  };
  const events: string[] = [];
  const onResult = vi.fn((received: StandardCrudOperationResult) => {
    expect(received).toBe(result);
    events.push('result');
  });
  const onCommitted = vi.fn(async () => { events.push('change'); });
  const refresh = vi.fn();
  await handoffStandardCrudSuccess({
    owner: { kind: 'action', resourceKey: 'directory' }, result, onResult, onCommitted,
    refresh, onSynchronizationError: vi.fn(),
  });
  expect(events).toEqual(['result', 'change']);
  expect(onResult).toHaveBeenCalledTimes(1);
  expect(onCommitted).toHaveBeenCalledTimes(1);
  expect(onCommitted).toHaveBeenCalledWith({
    resourceKey: 'directory', operation: 'delete', id: 'd', before: { parent: 'p' },
  });
  expect(refresh).not.toHaveBeenCalled();
});

describe.each(['action', 'mutation'] as const)('%s success handoff', (kind) => {
  const owner = { kind, resourceKey: 'directory' };
  const result: StandardCrudOperationResult = {
    operation: 'update', payload: {}, change: { operation: 'update', id: 'd' },
  };

  it.each(['no result', 'no change', 'no receiver'] as const)('refreshes once for %s without inventing a change', async (missing) => {
    const onCommitted = vi.fn();
    const refresh = vi.fn();
    const onResult = vi.fn();
    await handoffStandardCrudSuccess({
      owner,
      result: missing === 'no result' ? undefined : missing === 'no change' ? { operation: 'update', payload: {} } : result,
      onResult, onCommitted: missing === 'no receiver' ? undefined : onCommitted,
      refresh, onSynchronizationError: vi.fn(),
    });
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(onCommitted).not.toHaveBeenCalled();
    expect(onResult).toHaveBeenCalledTimes(missing === 'no result' ? 0 : 1);
  });

  it('continues after registration failure and reports a rejected handoff without fallback', async () => {
    const registrationError = new Error('presenter unavailable');
    const handoffError = new Error('read unavailable');
    const errors: unknown[] = [];
    const onCommitted = vi.fn().mockRejectedValue(handoffError);
    const refresh = vi.fn();
    await handoffStandardCrudSuccess({
      owner, result, onResult: () => { throw registrationError; }, onCommitted, refresh,
      onSynchronizationError: error => { errors.push(error); },
    });
    expect(errors).toEqual([registrationError, handoffError]);
    expect(onCommitted).toHaveBeenCalledTimes(1);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('continues to fallback after registration failure and reports both failures in order', async () => {
    const errors: unknown[] = [];
    const registrationError = new Error('presenter unavailable');
    const refreshError = new Error('refresh unavailable');
    await handoffStandardCrudSuccess({
      owner, result, onResult: () => { throw registrationError; },
      refresh: () => { throw refreshError; }, onSynchronizationError: error => { errors.push(error); },
    });
    expect(errors).toEqual([registrationError, refreshError]);
  });

  it('does not wait for promises returned by void result and refresh callbacks', async () => {
    const unfinished = new Promise<void>(() => {});
    const refresh = vi.fn(() => unfinished);
    await handoffStandardCrudSuccess({
      owner, result, onResult: () => unfinished, refresh, onSynchronizationError: vi.fn(),
    });
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});

it.each([undefined, ''])('falls back for an action without a truthy resource key (%s)', async (resourceKey) => {
  const onCommitted = vi.fn();
  const refresh = vi.fn();
  await handoffStandardCrudSuccess({
    owner: { kind: 'action', resourceKey },
    result: { operation: 'action', payload: {}, change: { operation: 'delete', id: 'd' } },
    onCommitted, refresh, onSynchronizationError: vi.fn(),
  });
  expect(onCommitted).not.toHaveBeenCalled();
  expect(refresh).toHaveBeenCalledTimes(1);
});

it('retains the mutation owner resource key without adding an action validity check', async () => {
  const onCommitted = vi.fn();
  const refresh = vi.fn();
  await handoffStandardCrudSuccess({
    owner: { kind: 'mutation', resourceKey: '' },
    result: { operation: 'create', payload: {}, change: { operation: 'create', id: 'd' } },
    onCommitted, refresh, onSynchronizationError: vi.fn(),
  });
  expect(onCommitted).toHaveBeenCalledWith({ operation: 'create', id: 'd', resourceKey: '' });
  expect(refresh).not.toHaveBeenCalled();
});

it.each([true, false])('refreshOnSuccess=false only disables fallback (change=%s)', async (hasChange) => {
  const onCommitted = vi.fn();
  const refresh = vi.fn();
  await handoffStandardCrudSuccess({
    owner: { kind: 'action', resourceKey: 'directory', refreshOnSuccess: false },
    result: { operation: 'action', payload: {}, ...(hasChange ? { change: { operation: 'delete', id: 'd' } as const } : {}) },
    onCommitted, refresh, onSynchronizationError: vi.fn(),
  });
  expect(onCommitted).toHaveBeenCalledTimes(hasChange ? 1 : 0);
  expect(refresh).not.toHaveBeenCalled();
});
