// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
it('hands off delete once and separates synchronization failures from a successful action', async () => {
  const committed = vi.fn().mockRejectedValue(new Error('read unavailable'));
  const handler = vi.fn().mockResolvedValue({ operation: 'action', payload: undefined, change: { operation: 'delete', id: 'd', before: { parent: 'p' } } });
  let execution!: ReturnType<typeof useStandardCrudActionExecution>;
  function Harness() { execution = useStandardCrudActionExecution({ refresh: vi.fn(), onCommitted: committed, resourceKey: 'directory' }); return null; }
  const container = document.createElement('div'); const root = createRoot(container);
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><Harness /></LocalizationProvider>));
    let result: unknown;
    await act(async () => { result = await execution.execute({ key: 'delete', handler, context: { id: 'd' } }); });
    expect(result).toMatchObject({ status: 'succeeded' }); expect(handler).toHaveBeenCalledTimes(1);
    expect(committed).toHaveBeenCalledTimes(1); expect(committed).toHaveBeenCalledWith({ resourceKey: 'directory', operation: 'delete', id: 'd', before: { parent: 'p' } });
    expect(execution.error).toBeNull(); expect(execution.synchronizationError).toContain('已保存');
  } finally { await act(async () => root.unmount()); }
});
