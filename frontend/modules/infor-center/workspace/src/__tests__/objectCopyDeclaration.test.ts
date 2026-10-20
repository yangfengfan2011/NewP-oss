import { afterEach, expect, it, vi } from 'vitest';
import { platformFetch } from '@icp-platform/core';
import { WorkspaceClipboardSession } from '../workspaceClipboard';

vi.mock('@icp-platform/core', () => ({ platformFetch: vi.fn() }));
const source = { spaceId: 'public', rootFolderId: 'root', linkIds: ['source'] };
const target = { ...source, linkIds: ['target'] };
const response = (body: unknown) => new Response(JSON.stringify(body));
afterEach(() => { vi.resetAllMocks(); });

it.each(['Part', 'Document'] as const)('sends the bound source only for actual %s prepare/submit and keeps recovery on its credential channel', async model => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(response({ items: [{ linkId: 'source-link', modelKey: model }] }));
  await session.copy(source, ['source-link'], 'select-copy', 'select-source');
  const draft = session.prepareObjectCopy(target, 'object-copy', 'object-source');
  vi.mocked(platformFetch).mockResolvedValueOnce(response({ operationId: 'op', modelKey: model,
    values: { ECODE: '', ENAME: 'Source' }, sourcePath: ['Source'], targetPath: ['Target'] }));
  const controller = new AbortController();
  await session.readObjectCopyInput(draft, controller.signal);
  const prepare = vi.mocked(platformFetch).mock.lastCall!;
  expect(new Headers(prepare[1]?.headers).get('X-NewP-Data-Declaration')).toBe('object-source');
  expect(prepare[1]?.signal).toBe(controller.signal);
  vi.mocked(platformFetch).mockRejectedValueOnce(new TypeError('lost receipt'));
  await expect(session.pasteObjectCopy(draft, { ECODE: 'NEW', ENAME: 'Copy' }, target, 'object-copy', 'object-source')).rejects.toThrow();
  const submit = vi.mocked(platformFetch).mock.lastCall!;
  expect(new Headers(submit[1]?.headers).get('X-NewP-Data-Declaration')).toBe('object-source');
  vi.mocked(platformFetch).mockResolvedValueOnce(response({ state: 'ready', succeeded: false }));
  await session.resolveObjectCopy();
  expect(new Headers(vi.mocked(platformFetch).mock.lastCall![1]?.headers).has('X-NewP-Data-Declaration')).toBe(false);
  vi.mocked(platformFetch).mockResolvedValueOnce(response({ state: 'cancelled', succeeded: false }));
  await session.resolveObjectCopy(true);
  expect(new Headers(vi.mocked(platformFetch).mock.lastCall![1]?.headers).has('X-NewP-Data-Declaration')).toBe(false);
  expect(draft.closed).toBe(true);
});

it.each(['Part', 'Document'])('requires an actual %s binding and rejects a draft submitted under another binding before HTTP', async model => {
  const session = new WorkspaceClipboardSession(vi.fn());
  vi.mocked(platformFetch).mockResolvedValueOnce(response({ items: [{ linkId: 'source-link', modelKey: model }] }));
  await session.copy(source, ['source-link'], 'select-copy');
  expect(() => session.prepareObjectCopy(target, 'object-copy')).toThrow();
  const draft = session.prepareObjectCopy(target, 'object-copy', 'original');
  vi.mocked(platformFetch).mockResolvedValueOnce(response({ operationId: 'op', values: { ECODE: '', ENAME: 'Source' }, sourcePath: ['Source'], targetPath: ['Target'] }));
  await session.readObjectCopyInput(draft, new AbortController().signal);
  const count = vi.mocked(platformFetch).mock.calls.length;
  await expect(session.pasteObjectCopy(draft, { ECODE: 'NEW' }, target, 'object-copy', 'different')).rejects.toThrow();
  expect(platformFetch).toHaveBeenCalledTimes(count);
});
