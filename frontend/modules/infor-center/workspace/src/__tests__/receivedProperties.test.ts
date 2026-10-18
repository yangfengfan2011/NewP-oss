// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import workspace from '../index';
import positions from '../../../../../../tests/Kernel.Tests/Fixtures/WorkspaceTransferFeatures.contract.json';

const scope = { spaceId: 'space', rootFolderId: 'root', linkIds: '["directory"]', sourceLinkId: 'receipt-reference',
  sourceObjectId: 'object-eid', sourceModelKey: 'Part', sourceFeaturePosition: 'untrusted-notification-header' };
afterEach(() => { vi.unstubAllGlobals(); });
it('uses the received-object binding qualified read position and never borrows the notification source header', async () => {
  const source = workspace.standardCrudDetailReadSources?.find(item => item.key === 'workspace.content.receivedProperties');
  expect(source).toBeDefined();
  const request = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({ resourceKey: 'part.part', objectId: 'object-eid',
    detail: { fields: { EID: 'object-eid', ENAME: '原对象' }, summaries: {} } }), { headers: { 'Content-Type': 'application/json' } }));
  vi.stubGlobal('fetch', request);
  const result = await source!.get('part.part', 'object-eid', { featurePosition: positions.readPosition, queryScope: scope });
  expect(result.normalized.fields.EID).toBe('object-eid');
  const [input, init] = request.mock.calls[0]!;
  const sent = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
  expect(sent.headers.get('X-NewP-Feature')).toBe(positions.readPosition);
  expect(new URL(sent.url).pathname).toBe('/api/workspace/content/receipt-reference/properties');
  expect(JSON.parse(new URL(sent.url).searchParams.get('queryScope')!)).toEqual({ spaceId: 'space', rootFolderId: 'root', linkIds: ['directory'] });
  request.mockClear();
  await expect(source!.get('part.part', 'object-eid', { queryScope: scope })).rejects.toThrow('读取位置');
  expect(request).not.toHaveBeenCalled();
});
