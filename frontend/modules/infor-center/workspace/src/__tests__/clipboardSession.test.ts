import { afterEach, expect, it, vi } from 'vitest';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { WorkspaceClipboardSession } from '../workspaceClipboard';
import { platformFetch } from '@icp-platform/core';

vi.mock('@icp-platform/core', () => ({ platformFetch: vi.fn() }));
const source = { spaceId: 'public', rootFolderId: 'root', linkIds: ['source-parent'] };
const target = { spaceId: 'public', rootFolderId: 'root', linkIds: ['target-parent'] };
const validated = (ids: string[]) => new Response(JSON.stringify({ mode: 'copy', sourcePosition: source, items: ids.map(linkId => ({ linkId })) }));
afterEach(() => { vi.resetAllMocks(); });

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
  const older = session.copy(source, ['old'], 'copy');
  await session.copy(source, ['new'], 'copy');
  finish(validated(['old'])); expect(await older).toBe(false);
  expect(session.getSnapshot().clipboard?.linkIds).toEqual(['new']);
  let finishClosed!: (value: Response) => void;
  vi.mocked(platformFetch).mockImplementationOnce(() => new Promise(resolve => { finishClosed = resolve; }));
  const closed = session.copy(source, ['late'], 'copy'); session.close();
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

it.each(['network', 'server', 'commit'])('locks an unknown %s result, blocks replay and permits only explicit clear or resampling afterward', async failure => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(validated(['lid'])); await session.copy(source, ['lid'], 'copy');
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

it.each([{}, { succeeded: false }, null])('treats a malformed 2xx paste receipt as unknown (%j)', async receipt => {
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
