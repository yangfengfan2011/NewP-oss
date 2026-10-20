import { afterEach, expect, it, vi } from 'vitest';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { WorkspaceClipboardSession } from '../workspaceClipboard';
import { platformFetch } from '@icp-platform/core';

import compiled from '../../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
const declaration = compiled.declarations.find(item => item.viewKey === 'workspace.folder.list' && item.surface === 'pageActions' && item.actionKey === 'pasteObjectCopy')!;

vi.mock('@icp-platform/core', () => ({ platformFetch: vi.fn() }));
const source = { spaceId: 'public', rootFolderId: 'root', linkIds: ['source-parent'] };
const target = { spaceId: 'public', rootFolderId: 'root', linkIds: ['target-parent'] };
it('recovers a lost object-copy receipt by reading its operation and never repeats the write', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'part', modelKey: 'Part' }] })));
  await session.copy(source, ['part'], 'copy');
  const draft = session.prepareObjectCopy(target, 'object-copy', 'part-source');
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'operation', values: { ECODE: '', ENAME: 'Source', Description: 'Original' }, sourcePath: ['Public', 'Source'], targetPath: ['Public', 'Target'] })));
  await session.readObjectCopyInput(draft, new AbortController().signal);
  vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('lost receipt'));
  await expect(session.pasteObjectCopy(draft, { ECODE: 'NEW', ENAME: 'My edit' }, target, 'object-copy', 'part-source')).rejects.toThrow();
  await expect(session.pasteObjectCopy(draft, { ECODE: 'REPLAY', ENAME: 'Must not write' }, target, 'object-copy', 'part-source')).rejects.toThrow();
  expect(platformFetch).toHaveBeenCalledTimes(3);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ state: 'committed', succeeded: true })));
  expect(await session.resolveObjectCopy()).toEqual({ state: 'committed', succeeded: true });
  expect(platformFetch).toHaveBeenLastCalledWith('/api/workspace/content/objects/copy/result', expect.objectContaining({ body: JSON.stringify({ operationId: 'operation' }) }));
  expect(session.getSnapshot().phase).toBe('idle');
  expect(session.getSnapshot().clipboard?.linkIds).toEqual(['part']);
});
it.each(['copy', 'copy-structure'] as const)('freezes %s a named copy against changed target, source snapshot, ended instance and unknown outcomes', async kind => {
  const session = new WorkspaceClipboardSession(vi.fn());
  expect(() => session.prepareFolderCopy(target, 'copy-folder', kind, 'copy-declaration')).toThrow();
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'folder', modelKey: 'WorkspaceFolder' }] })));
  await session.copy(source, ['folder'], 'copy');
  const draft = session.prepareFolderCopy(target, 'copy-folder', kind, 'copy-declaration');
  await expect(session.pasteFolderCopy(draft, 'New', { ...target, linkIds: ['elsewhere'] }, 'copy-folder', kind, undefined, 'copy-declaration')).rejects.toThrow();
  await expect(session.pasteFolderCopy(draft, 'Wrong action', target, 'copy-folder', kind === 'copy' ? 'copy-structure' : 'copy', undefined, 'copy-declaration')).rejects.toThrow();
  expect(platformFetch).toHaveBeenCalledTimes(1);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ succeeded: true })));
  await session.pasteFolderCopy(draft, 'New', target, 'copy-folder', kind, undefined, 'copy-declaration');
  expect(session.getSnapshot().clipboard?.linkIds).toEqual(['folder']);
  const second = session.prepareFolderCopy(target, 'copy-folder', kind, 'copy-declaration');
  vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('lost'));
  await expect(session.pasteFolderCopy(second, 'Another', target, 'copy-folder', kind, undefined, 'copy-declaration')).rejects.toThrow();
  expect(session.getSnapshot().phase).toBe('unknown');
  session.close();
  await expect(session.pasteFolderCopy(second, 'Again', target, 'copy-folder', kind, undefined, 'copy-declaration')).rejects.toThrow();
  expect(platformFetch).toHaveBeenCalledTimes(3);
});
const validated = (ids: string[]) => new Response(JSON.stringify({ mode: 'copy', sourcePosition: source, items: ids.map(linkId => ({ linkId })) }));
it.each(['snapshot', 'closed'] as const)('discards a delayed preparation after its %s identity expires', async reason => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'folder', modelKey: 'WorkspaceFolder' }] })));
  await session.copy(source, ['folder'], 'copy'); const draft = session.prepareFolderCopy(target, 'named-copy', 'copy', 'copy-declaration');
  let finish!: (response: Response) => void;
  vi.mocked(platformFetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const controller = new AbortController(); const reading = session.readFolderCopyInput(draft, controller.signal);
  expect(platformFetch).toHaveBeenLastCalledWith('/api/workspace/content/folders/copy/prepare', expect.objectContaining({ signal: controller.signal,
    headers: expect.objectContaining({ 'X-NewP-Feature': 'named-copy' }), body: JSON.stringify({ mode: 'copy', sourcePosition: source, linkIds: ['folder'], targetPosition: target }) }));
  if (reason === 'closed') draft.closed = true;
  else { vi.mocked(platformFetch).mockResolvedValueOnce(validated(['replacement'])); await session.copy(source, ['replacement'], 'copy'); }
  finish(new Response(JSON.stringify({ sourceName: 'old', sourcePath: ['Public', 'old'], targetPath: ['Public', 'target'] })));
  await expect(reading).rejects.toThrow();
  expect(vi.mocked(platformFetch).mock.calls.some(([path]) => String(path).endsWith('/folders/copy'))).toBe(false);
});
afterEach(() => { vi.resetAllMocks(); });

it('allows one Document and keeps an interrupted preparation locked until controlled cancellation', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'document', modelKey: 'Document' }] })));
  await session.copy(source, ['document'], 'copy');
  expect(session.canCopyObject()).toBe(true);
  const draft = session.prepareObjectCopy(target, 'object-copy', declaration.id);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'document-operation', modelKey: 'Document', fileCount: 3, totalBytes: 12,
    values: { ECODE: '', ENAME: 'Document' }, sourcePath: ['Source'], targetPath: ['Target'] })));
  await session.readObjectCopyInput(draft, new AbortController().signal);
  vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('lost while preparing'));
  await expect(session.pasteObjectCopy(draft, { ECODE: 'NEW', ENAME: 'Document' }, target, 'object-copy', declaration.id)).rejects.toThrow();
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ state: 'preparing', succeeded: false })));
  expect((await session.resolveObjectCopy()).state).toBe('preparing');
  expect(session.canResolveObjectCopy()).toBe(true);
  expect(session.canCopyObject()).toBe(false);
  const count = vi.mocked(platformFetch).mock.calls.length;
  await expect(session.pasteObjectCopy(draft, { ECODE: 'REPLAY' }, target, 'object-copy', declaration.id)).rejects.toThrow();
  expect(platformFetch).toHaveBeenCalledTimes(count);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ state: 'cancelled', succeeded: false })));
  await session.resolveObjectCopy(true);
  expect(draft.closed).toBe(true);
  expect(session.canResolveObjectCopy()).toBe(false);
  expect(session.canCopyObject()).toBe(true);
});

it('requires every selected Document in the prepared input instead of silently using the first', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'a', modelKey: 'Document' }, { linkId: 'b', modelKey: 'Document' }] })));
  await session.copy(source, ['a', 'b'], 'copy');
  expect(session.canCopyObject()).toBe(true);
  const draft = session.prepareObjectCopy(target, 'object-copy', declaration.id);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'partial', modelKey: 'Document', values: { ECODE: '', ENAME: 'Only first' }, sourcePath: ['First'], targetPath: ['Target'] })));
  await expect(session.readObjectCopyInput(draft, new AbortController().signal)).rejects.toThrow();
  expect(vi.mocked(platformFetch).mock.calls.some(([url]) => String(url).endsWith('/objects/copy'))).toBe(false);
});

it.each([['Part', 'Part'], ['Part', 'Document'], ['Document', 'WorkspaceFolder']])('retains mixed reference selection but rejects true-copy for %s/%s', async (first, second) => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'a', modelKey: first }, { linkId: 'b', modelKey: second }] })));
  await session.copy(source, ['a', 'b'], 'copy');
  expect(session.getSnapshot().clipboard?.linkIds).toEqual(['a', 'b']); expect(session.canCopyObject()).toBe(false);
  expect(() => session.prepareObjectCopy(target, 'object-copy', declaration.id)).toThrow();
});
it.each([false, true])('can cancel a prepared input and locks an unknown cancellation before any business write (unknown=%s)', async unknown => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'document', modelKey: 'Document' }] })));
  await session.copy(source, ['document'], 'copy'); const draft = session.prepareObjectCopy(target, 'paste-feature', declaration.id);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'prepared-input', values: { ECODE: '', ENAME: 'Source' }, sourcePath: ['Source'], targetPath: ['Target'] })));
  await session.readObjectCopyInput(draft, new AbortController().signal);
  if (unknown) {
    vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('cancellation receipt lost'));
    await expect(session.resolveObjectCopy(true)).rejects.toThrow();
    expect(session.getSnapshot().phase).toBe('unknown');
    const count = vi.mocked(platformFetch).mock.calls.length;
    await expect(session.pasteObjectCopy(draft, { ECODE: 'NO-REPLAY' }, target, 'paste-feature', declaration.id)).rejects.toThrow();
    expect(platformFetch).toHaveBeenCalledTimes(count);
  }
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ state: 'cancelled', succeeded: false })));
  await session.resolveObjectCopy(true);
  expect(draft.closed).toBe(true);
  expect(vi.mocked(platformFetch).mock.calls.some(([url]) => String(url).endsWith('/objects/copy'))).toBe(false);
});

it('never restores an old selection when an acknowledged cancellation receives its late failure', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'old', modelKey: 'Document' }] })));
  await session.copy(source, ['old'], 'copy'); const draft = session.prepareObjectCopy(target, 'paste-feature', declaration.id);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'cancel-inflight', values: { ECODE: '', ENAME: 'Source' }, sourcePath: ['Source'], targetPath: ['Target'] })));
  await session.readObjectCopyInput(draft, new AbortController().signal);
  let fail!: (failure: Error) => void;
  vi.mocked(platformFetch).mockImplementationOnce(() => new Promise((_, reject) => { fail = reject; }));
  const pending = session.pasteObjectCopy(draft, { ECODE: 'NEW', ENAME: 'Copy' }, target, 'paste-feature', declaration.id).catch(failure => failure);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ state: 'cancelled', succeeded: false })));
  await session.resolveObjectCopy(true);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'replacement', modelKey: 'Document' }] })));
  await session.copy(source, ['replacement'], 'copy');
  fail(new TypeError('late disconnect')); await pending;
  expect(session.getSnapshot().clipboard?.linkIds).toEqual(['replacement']);
});

it.each([false, true])('cut uses its own qualification and clears only a confirmed success (noChange=%s)', async noChange => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['lid']));
  await session.cut(source, ['lid'], 'cut-feature');
  expect(platformFetch).toHaveBeenLastCalledWith('/api/workspace/content/clipboard/cut/validate', expect.objectContaining({
    headers: expect.objectContaining({ 'X-NewP-Feature': 'cut-feature' }), body: JSON.stringify({ mode: 'cut', sourcePosition: source, linkIds: ['lid'] }),
  }));
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ code: 'Workspace.ObjectWriteDenied' }), { status: 403 }));
  await expect(session.paste(target, 'paste')).rejects.toThrow();
  expect(session.getSnapshot()).toMatchObject({ phase: 'idle', clipboard: { mode: 'cut', linkIds: ['lid'] } });
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ succeeded: true, noChange })));
  expect(await session.paste(target, 'paste')).toEqual({ succeeded: true, noChange });
  expect(session.getSnapshot()).toEqual({ phase: 'idle' });
});

it.each(['copy', 'cut'] as const)('uses each current %s and paste declaration without reusing source qualification as authority', async mode => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['lid']));
  await session[mode](source, ['lid'], 'source-feature', 'source-declaration');
  expect(platformFetch).toHaveBeenLastCalledWith(mode === 'copy' ? '/api/workspace/content/clipboard/validate' : '/api/workspace/content/clipboard/cut/validate', expect.objectContaining({
    headers: expect.objectContaining({ 'X-NewP-Feature': 'source-feature', 'X-NewP-Data-Declaration': 'source-declaration' }),
  }));
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ succeeded: true })));
  await session.paste(target, 'target-feature', 'paste-declaration');
  expect(platformFetch).toHaveBeenLastCalledWith('/api/workspace/content/paste', expect.objectContaining({
    headers: expect.objectContaining({ 'X-NewP-Feature': 'target-feature', 'X-NewP-Data-Declaration': 'paste-declaration' }),
  }));
  const requests = vi.mocked(platformFetch).mock.calls.length;
  expect(session.clear()).toBe(true);
  expect(platformFetch).toHaveBeenCalledTimes(requests);
});

it('replaces only after a successful qualification and preserves both source and validated selection order', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['b', 'a']));
  await session.copy(source, ['b', 'a'], 'copy');
  expect(session.getSnapshot().clipboard).toEqual({ mode: 'copy', sourcePosition: source, linkIds: ['b', 'a'] });
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ code: 'Workspace.ObjectCopyDenied' }), { status: 403 }));
  await expect(session.copy(source, ['new'], 'copy')).rejects.toBeInstanceOf(StandardCrudRequestError);
  expect(session.getSnapshot().clipboard?.linkIds).toEqual(['b', 'a']);
  expect(session.getSnapshot().phase).toBe('idle');
});

it('discards delayed older validation and never revives a closed session', async () => {
  const changed = vi.fn(); const session = new WorkspaceClipboardSession(changed);
  let finish!: (value: Response) => void;
  vi.mocked(platformFetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValueOnce(validated(['new']));
  const older = session.cut(source, ['old'], 'cut');
  await session.copy(source, ['new'], 'copy');
  finish(validated(['old'])); expect(await older).toBe(false);
  expect(session.getSnapshot().clipboard?.linkIds).toEqual(['new']);
  let finishClosed!: (value: Response) => void;
  vi.mocked(platformFetch).mockImplementationOnce(() => new Promise(resolve => { finishClosed = resolve; }));
  const closed = session.cut(source, ['late'], 'cut'); session.close();
  const count = changed.mock.calls.length;
  finishClosed(validated(['late'])); expect(await closed).toBe(false);
  expect(session.getSnapshot().clipboard).toBeUndefined(); expect(changed).toHaveBeenCalledTimes(count);
});

it.each([400, 403, 404, 409])('retains a correctable explicit %s failure and keeps successful copy snapshots for further pastes', async status => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['lid'])); await session.copy(source, ['lid'], 'copy');
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ code: 'Workspace.DuplicateReference' }), { status }));
  await expect(session.paste(target, 'paste')).rejects.toBeInstanceOf(StandardCrudRequestError);
  expect(session.getSnapshot()).toMatchObject({ phase: 'idle', clipboard: { linkIds: ['lid'] } });
  vi.mocked(platformFetch).mockImplementation(async () => new Response(JSON.stringify({ succeeded: true })));
  await session.paste(target, 'paste'); await session.paste({ ...target, linkIds: ['another-parent'] }, 'paste');
  expect(session.getSnapshot()).toMatchObject({ phase: 'idle', clipboard: { linkIds: ['lid'] } });
});

it.each((['copy', 'cut'] as const).flatMap(mode => ['network', 'server', 'commit'].map(failure => [mode, failure] as const)))('locks an unknown %s %s result, blocks replay and permits only explicit clear or resampling afterward', async (mode, failure) => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['lid'])); await session[mode](source, ['lid'], mode);
  if (failure === 'network') vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('Connection lost'));
  else vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ code: failure === 'commit' ? 'Persistence.CommitOutcomeUnknown' : 'HTTP_500' }), { status: failure === 'commit' ? 409 : 500 }));
  await expect(session.paste(target, 'paste')).rejects.toThrow();
  expect(session.getSnapshot().phase).toBe('unknown');
  const calls = vi.mocked(platformFetch).mock.calls.length;
  await expect(session.paste(target, 'paste')).rejects.toThrow(); expect(platformFetch).toHaveBeenCalledTimes(calls);
  session.clear(); expect(session.getSnapshot()).toEqual({ phase: 'idle' });
});

it('freezes the submitted snapshot and blocks clearing, replacement and another write while paste is in flight', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['lid'])); await session.copy(source, ['lid'], 'copy');
  let finish!: (value: Response) => void;
  vi.mocked(platformFetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const pasting = session.paste(target, 'paste');
  session.clear(); expect(session.getSnapshot().clipboard?.linkIds).toEqual(['lid']);
  await expect(session.copy(source, ['other'], 'copy')).rejects.toThrow(); await expect(session.paste(target, 'paste')).rejects.toThrow();
  finish(new Response(JSON.stringify({ succeeded: true }))); await pasting;
  const request = JSON.parse(String(vi.mocked(platformFetch).mock.calls[1][1]?.body));
  expect(request).toEqual({ mode: 'copy', sourcePosition: source, linkIds: ['lid'], targetPosition: target });
});

it('preserves an unknown lock after failed resampling and unlocks only a successful new qualification', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['old'])); await session.copy(source, ['old'], 'copy');
  vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('Connection lost'));
  await expect(session.paste(target, 'paste')).rejects.toThrow();
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ code: 'Workspace.ObjectCopyDenied' }), { status: 403 }));
  await expect(session.copy(source, ['new'], 'copy')).rejects.toThrow();
  expect(session.getSnapshot()).toMatchObject({ phase: 'unknown', clipboard: { linkIds: ['old'] } });
  const calls = vi.mocked(platformFetch).mock.calls.length;
  await expect(session.paste(target, 'paste')).rejects.toThrow(); expect(platformFetch).toHaveBeenCalledTimes(calls);
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['new'])); await session.copy(source, ['new'], 'copy');
  expect(session.getSnapshot()).toMatchObject({ phase: 'idle', clipboard: { linkIds: ['new'] } });
});

it.each([{}, { succeeded: false }, { succeeded: true, noChange: 'true' }, null])('treats a malformed 2xx paste receipt as unknown (%j)', async receipt => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['lid'])); await session.copy(source, ['lid'], 'copy');
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify(receipt)));
  await expect(session.paste(target, 'paste')).rejects.toThrow();
  expect(session.getSnapshot().phase).toBe('unknown');
});

it('keeps the original unknown state across superseded validating requests and a failed latest selection', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['old'])); await session.copy(source, ['old'], 'copy');
  vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('lost')); await expect(session.paste(target, 'paste')).rejects.toThrow();
  let finish!: (value: Response) => void;
  vi.mocked(platformFetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'Workspace.ReferenceUnavailable' }), { status: 404 }));
  const first = session.copy(source, ['first'], 'copy');
  await expect(session.copy(source, ['second'], 'copy')).rejects.toThrow();
  finish(validated(['first'])); expect(await first).toBe(false);
  expect(session.getSnapshot()).toMatchObject({ phase: 'unknown', clipboard: { linkIds: ['old'] } });
  await expect(session.paste(target, 'paste')).rejects.toThrow();
});

it('requires the named-copy declaration before preparation and rejects a changed declaration before submit', async () => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'folder', modelKey: 'WorkspaceFolder' }] })));
  await session.copy(source, ['folder'], 'copy');
  expect(() => session.prepareFolderCopy(target, 'named-copy')).toThrow();
  expect(platformFetch).toHaveBeenCalledTimes(1);
  const draft = session.prepareFolderCopy(target, 'named-copy', 'copy', 'first-declaration');
  await expect(session.pasteFolderCopy(draft, 'stale', target, 'named-copy', 'copy', true, 'second-declaration')).rejects.toThrow();
  expect(platformFetch).toHaveBeenCalledTimes(1);
});
