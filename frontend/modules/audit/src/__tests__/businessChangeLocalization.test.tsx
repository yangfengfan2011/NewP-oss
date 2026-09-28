// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { formatRequestError, useLocalization } from '@icp-platform/core';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuditLocalization } from './auditLocalization';
import { changesRenderer, createBusinessChangeResource } from '../businessChangeResource';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
it('retranslates existing changes and query errors without replaying the request or losing values', async () => {
  const getComputedStyle = window.getComputedStyle.bind(window);
  const style = vi.spyOn(window, 'getComputedStyle').mockImplementation(element => getComputedStyle(element));
  onTestFinished(() => style.mockRestore());
  Object.defineProperty(window, 'matchMedia', { configurable: true, value: () => ({ matches: false, addListener() {}, removeListener() {} }) });
  const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(new Response(JSON.stringify({ status: 400, title: '时间范围必须为有序 UTC 时间点。', code: 'Audit.InvalidTimeRange', reasonKey: 'BusinessChangeView.InvalidTimeRange' }), { status: 400 }));
  const resource = createBusinessChangeResource(fetcher);
  const failure = await resource.dataAdapter.list({ From: 'invalid' }).catch(error => error);
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); });
  function Consumer() {
    const { t, setLanguage } = useLocalization();
    return <><button onClick={() => setLanguage('en')}>English test</button><div role="alert">{formatRequestError(failure, t)}</div>
      {changesRenderer({ blockKey: 'changes', title: t('BusinessChangeView.Block_Changes'), block: { status: 'ready', data: [
        { field: 'ENAME', before: null, after: { kind: 'text', value: '历史姓名' } },
      ] } })}</>;
  }
  await act(async () => root.render(<AuditLocalization><Consumer /></AuditLocalization>));
  expect(container.textContent).toContain('起始和结束时间必须为 UTC 时间点，且起始时间不得晚于结束时间。');
  await act(async () => container.querySelector('button')!.click());
  expect(container.textContent).toContain('Enter UTC timestamps with the start at or before the end.');
  expect(container.textContent).toContain('Previous value');
  expect(container.textContent).toContain('历史姓名');
  expect(container.textContent).not.toContain('原值');
  expect(fetcher).toHaveBeenCalledTimes(1);
});
