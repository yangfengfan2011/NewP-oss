import { afterEach, expect, it, vi } from 'vitest';
import resource from '../workspaceRootResource';

afterEach(() => { vi.unstubAllGlobals(); });
it('resolves the same EID to the registered owning detail view without confusing LID', () => {
  const target = resource.dataAdapter.getDetailTarget;
  expect(target).toBeTypeOf('function');
  expect(target!({ LID: 'part-link', ObjectModelKey: 'Part/Part', ObjectId: 'same-id' })).toEqual({ detailViewKey: 'part.part.detail', id: 'same-id' });
  expect(target!({ LID: 'document-link', ObjectModelKey: 'Document/Document', ObjectId: 'same-id' })).toEqual({ detailViewKey: 'document.document.detail', id: 'same-id' });
  expect(() => target!({ ObjectModelKey: 'Unknown/Type', ObjectId: 'same-id' })).toThrow();
});
it('uses LID as row identity and the associated Entity EID for properties', async () => {
  const request = vi.fn(async (_input: RequestInfo | URL) => new Response(JSON.stringify({ items: [{ linkId: 'relation',
    parent: { modelKey: 'Workspace/WorkspaceFolder', id: 'root' }, child: { modelKey: 'Workspace/WorkspaceFolder', id: 'folder' },
    associatedObject: { modelKey: 'Workspace/WorkspaceFolder', id: 'folder' }, fields: { LID: 'relation', Name: '子文件夹', Code: null } }],
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
