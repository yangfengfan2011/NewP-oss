// @vitest-environment jsdom
import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider, platformFetch } from '@icp-platform/core';
import type { StandardCrudActionInstance } from '@icp-platform/sdk';
import WorkspaceObjectCopyRecovery from '../WorkspaceObjectCopyRecovery';
import { workspaceClipboard } from '../workspaceClipboard';
import dictionary from '../../metadata/dictionaries/Workspace.dic.json';

vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: vi.fn() }));

it.each([false, true])('recovers without replay and never lets a stale read overwrite cancellation (strict=%s)', async strict => {
  const entries = new Map<symbol, unknown>();
  const instance: StandardCrudActionInstance = { active: true, changed: vi.fn(), getOrCreate: (key, create) => {
    if (!entries.has(key)) entries.set(key, create()); return entries.get(key) as ReturnType<typeof create>;
  } };
  const session = workspaceClipboard(instance);
  const source = { spaceId: 'public', rootFolderId: 'root', linkIds: ['source'] };
  const target = { ...source, linkIds: ['target'] };
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'document', modelKey: 'Document' }] })));
  await session.copy(source, ['document'], 'select-feature');
  const draft = session.prepareObjectCopy(target, 'paste-feature');
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'durable-operation', values: { ECODE: '', ENAME: '源' }, sourcePath: ['源'], targetPath: ['目标'] })));
  await session.readObjectCopyInput(draft, new AbortController().signal);
  vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('lost receipt'));
  await expect(session.pasteObjectCopy(draft, { ECODE: 'NEW', ENAME: '副本' }, target, 'paste-feature')).rejects.toThrow();
  let finishOld!: (response: Response) => void;
  if (strict) vi.mocked(platformFetch).mockImplementationOnce(() => new Promise(resolve => { finishOld = resolve; }));
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ state: 'preparing', succeeded: false })));
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  const draftState = vi.fn(); const change = vi.fn();
  try {
    const view = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]}
      catalogs={{ zhs: Object.fromEntries(Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs])) }}>
      <WorkspaceObjectCopyRecovery instance={instance} featurePosition="result-feature" action={{ key: 'resolveObjectCopy', labelKey: 'Workspace.CopyResolve' }}
        value={undefined} onChange={change} onDraftStateChange={draftState} />
    </LocalizationProvider>;
    await act(async () => root.render(strict ? <StrictMode>{view}</StrictMode> : view));
    expect(element.textContent).toContain('也可能已中断');
    expect(draftState).toHaveBeenLastCalledWith({ dirty: false, ready: false });
    vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('read failed'));
    await act(async () => [...element.querySelectorAll('button')].find(button => button.textContent === '核对副本结果')!.click());
    expect(element.textContent).toContain('提交结果待核对');
    vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ state: 'cancelled', succeeded: false })));
    await act(async () => [...element.querySelectorAll('button')].find(button => button.textContent === '请求取消副本')!.click());
    expect(draftState).toHaveBeenLastCalledWith({ dirty: false, ready: true });
    expect(change).toHaveBeenLastCalledWith({ state: 'cancelled', succeeded: false });
    if (strict) {
      await act(async () => finishOld(new Response(JSON.stringify({ state: 'preparing', succeeded: false }))));
      expect(change).toHaveBeenLastCalledWith({ state: 'cancelled', succeeded: false });
      expect(draftState).toHaveBeenLastCalledWith({ dirty: false, ready: true });
    }
    expect(platformFetch).toHaveBeenLastCalledWith('/api/workspace/content/objects/copy/cancel', expect.objectContaining({ headers: expect.objectContaining({ 'X-NewP-Feature': 'paste-feature' }) }));
    expect(vi.mocked(platformFetch).mock.calls.filter(([url]) => String(url).endsWith('/objects/copy'))).toHaveLength(1);
  } finally { await act(async () => root.unmount()); element.remove(); vi.resetAllMocks(); }
});
