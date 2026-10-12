import { afterEach, expect, it, vi } from 'vitest';
import resource from '../workspaceRootResource';

afterEach(() => { vi.unstubAllGlobals(); });
it('resolves the same EID to the registered owning detail view without confusing LID', () => {
  const target = resource.dataAdapter.getDetailTarget;
  expect(target).toBeTypeOf('function');
  expect(target!({ LID: 'part-link', ObjectModelKey: 'Part', ObjectId: 'same-id' })).toEqual({ detailViewKey: 'part.part.detail', id: 'same-id' });
  expect(target!({ LID: 'document-link', ObjectModelKey: 'Document', ObjectId: 'same-id' })).toEqual({ detailViewKey: 'document.document.detail', id: 'same-id' });
  expect(() => target!({ ObjectModelKey: 'Unknown/Type', ObjectId: 'same-id' })).toThrow();
});
it('uses LID as row identity and the associated Entity EID for properties', async () => {
  const request = vi.fn(async (_input: RequestInfo | URL) => new Response(JSON.stringify({ items: [{ linkId: 'relation',
    parent: { modelKey: 'WorkspaceFolder', id: 'root' }, child: { modelKey: 'WorkspaceFolder', id: 'folder' },
    associatedObject: { modelKey: 'WorkspaceFolder', id: 'folder' }, fields: { LID: 'relation', Name: '子文件夹', Code: null } }],
    total: 1, page: 2, pageSize: 20 })));
  vi.stubGlobal('fetch', request);
  const result = await resource.dataAdapter.list({ fields: 'LID,Name', pageIndex: 2 }, {
    featurePosition: 'read', queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '[]', anchorId: 'root' },
  });
  expect(resource.dataAdapter.getRowId(result.items[0])).toBe('relation');
  expect(result.items[0].ObjectId).toBe('folder');
  const url = new URL(String(request.mock.calls[0][0]), 'http://localhost');
  expect(url.pathname).toBe('/api/links/workspace.content');
  expect(url.searchParams.get('anchorId')).toBe('root');
  expect(url.searchParams.get('fields')).toBe('LID,Name');
  expect(JSON.parse(url.searchParams.get('queryScope')!)).toEqual({ spaceId: 'public', rootFolderId: 'root', linkIds: '[]' });
});

it('edits and batch deletes through the current occurrence using LIDs', async () => {
  const request = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(String(_input).endsWith('antiforgery-token') ? { requestToken: 'token' } : { succeeded: true })));
  vi.stubGlobal('fetch', request);
  const context = { featurePosition: 'write', queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' } };
  expect(resource.actionHandlers?.editObject).toBeTypeOf('function');
  expect(resource.actionHandlers?.deleteObjects).toBeTypeOf('function');
  await resource.actionHandlers!.editObject({ ...context, id: 'relation', input: { ENAME: 'changed', Description: 'note' } });
  await resource.actionHandlers!.deleteObjects({ ...context, selectedRows: [{ LID: 'a', ObjectId: 'entity-a' }, { LID: 'b', ObjectId: 'entity-b' }] });
  const writes = await Promise.all(request.mock.calls.filter(([input]) => String(input instanceof Request ? input.url : input).includes('/api/workspace/content/')).map(async ([input, init]) => input instanceof Request ? input.clone().json() : JSON.parse(String(init?.body))));
  expect(writes).toEqual([
    { position: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, values: { ENAME: 'changed', Description: 'note' } },
    { position: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, linkIds: ['a', 'b'] },
  ]);
});
