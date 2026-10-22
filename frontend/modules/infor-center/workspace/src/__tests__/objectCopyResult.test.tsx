// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import WorkspaceObjectCopyResult from '../WorkspaceObjectCopyResult';
import workspaceDictionary from '../../metadata/dictionaries/Workspace.dic.json';

it.each([false, true])('presents matching rows without reading hidden objects and closes once (recovered=%s)', async recovered => {
  const close = vi.fn(); const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  const items = ['private-lid-a', 'private-lid-b'].map(sourceLinkId => ({ sourceLinkId, succeeded: true, state: 'committed' }));
  const rows = [{ sourceLinkId: 'private-lid-a', values: { ECODE: 'NEW-A', ENAME: '确认的 A' } }, { sourceLinkId: 'private-lid-b', values: { ECODE: 'NEW-B', ENAME: '确认的 B' } }];
  const catalogs = { zhs: Object.fromEntries(Object.entries(workspaceDictionary.Workspace).map(([key, entry]) => [`Workspace.${key}`, entry.zhs])) };
  try {
    await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
      <WorkspaceObjectCopyResult close={close} result={{ operation: 'action', payload: { kind: 'workspace.objectCopy', receipt: { state: 'committed', succeeded: true, items }, rows: recovered ? undefined : rows } }} />
    </LocalizationProvider>));
    const dialog = document.querySelector('[role="dialog"]')!;
    expect(dialog.querySelectorAll('li')).toHaveLength(2); expect(dialog.textContent).not.toContain('private-lid');
    if (!recovered) { expect(dialog.textContent).toContain('NEW-A · 确认的 A'); expect(dialog.textContent).toContain('NEW-B · 确认的 B'); }
    expect(dialog.textContent).toContain('确认顺序');
    await act(async () => [...dialog.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '关闭')!.click());
    expect(close).toHaveBeenCalledTimes(1);
  } finally { await act(async () => root.unmount()); element.remove(); }
});