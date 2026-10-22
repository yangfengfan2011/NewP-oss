// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import type { StandardCrudOperationResult } from '@icp-platform/sdk';
import { CompositionContext, type CompositionRuntime } from '../composition/pageCompositionRuntime';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import message from 'antd/es/message';
import { PageComposition } from '../composition/PageCompositionContext';
import { platformTestCatalogs } from './localizationTestCatalogs';

const auth = vi.hoisted(() => ({ subject: 'first' }));
vi.mock('../contexts/AuthContext', () => ({ useOptionalAuth: () => ({ user: { id: auth.subject } }) }));

it.each([
  { composed: false, language: 'zhs', changed: 2, unresolved: 1 },
  { composed: true, language: 'zhs', changed: 2, unresolved: 1 },
  { composed: false, language: 'en', changed: 0, unresolved: 0 },
  { composed: true, language: 'en', changed: 0, unresolved: 0 },
])('renders ownership notice counts for $language with composition=$composed', async ({ composed, language, changed, unresolved }) => {
  function Action() {
    const execution = useStandardCrudActionExecution({ refresh: vi.fn() });
    return <button onClick={() => void execution.execute({ key: 'ownership', context: {},
      handler: async () => ({ operation: 'action', payload: {}, noChange: true,
        notice: { reasonKey: 'Platform.OwnershipBatchCompleted', args: { changed, unresolved } } }),
    })}>Run</button>;
  }
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element:
    <LocalizationProvider defaultLanguage="zhs" initialLanguage={language}
      supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]} catalogs={platformTestCatalogs}>
      {composed ? <PageComposition><Action /></PageComposition> : <Action />}
    </LocalizationProvider>,
  }]);
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
    const expected = language === 'zhs' ? '已补齐 2 个对象，1 个对象需人工指定账号。'
      : 'Initialized 0 objects. 0 objects need an account selected manually.';
    await vi.waitFor(() => expect(document.querySelector('.ant-message-notice-content')?.textContent).toBe(expected));
  } finally {
    await act(async () => { message.destroy(); root.unmount(); }); router.dispose(); container.remove();
  }
});

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
