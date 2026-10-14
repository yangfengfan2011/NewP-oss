// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import type { StandardCrudOperationResult } from '@icp-platform/sdk';
import { CompositionContext, type CompositionRuntime } from '../composition/pageCompositionRuntime';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';

const auth = vi.hoisted(() => ({ subject: 'first' }));
vi.mock('../contexts/AuthContext', () => ({ useOptionalAuth: () => ({ user: { id: auth.subject } }) }));

it.each(['unmount', 'new context', 'new subject'] as const)('does not publish an old action notice after %s', async invalidation => {
  auth.subject = 'first';
  let resolve!: (value: StandardCrudOperationResult) => void;
  const response = new Promise<StandardCrudOperationResult>(complete => { resolve = complete; });
  const notice = vi.fn(); const refresh = vi.fn();
  const context = (): CompositionRuntime => ({ notice, report: vi.fn(), leave: proceed => proceed(),
    subscribe: () => () => {}, committed: vi.fn() });
  function Action() {
    const execution = useStandardCrudActionExecution({ refresh });
    return <button onClick={() => void execution.execute({ key: 'test', context: {}, handler: () => response })}>Run</button>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const render = (value: CompositionRuntime) => <LocalizationProvider defaultLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    <CompositionContext.Provider value={value}><Action /></CompositionContext.Provider>
  </LocalizationProvider>;
  let mounted = true; const current = context();
  try {
    await act(async () => root.render(render(current)));
    await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
    if (invalidation === 'unmount') { await act(async () => root.unmount()); mounted = false; }
    else if (invalidation === 'new context') await act(async () => root.render(render(context())));
    else { auth.subject = 'second'; await act(async () => root.render(render(current))); }
    await act(async () => resolve({ operation: 'action', payload: {}, noChange: true, notice: { reasonKey: 'Workspace.TreeOrderFirst' } }));
    expect(notice).not.toHaveBeenCalled(); expect(refresh).not.toHaveBeenCalled();
  } finally { if (mounted) await act(async () => root.unmount()); container.remove(); }
});
