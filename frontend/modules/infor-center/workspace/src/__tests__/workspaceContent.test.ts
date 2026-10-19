import { afterEach, expect, it, vi } from 'vitest';
import resource from '../workspaceRootResource';
import { StandardCrudRequestError } from '@icp-platform/sdk';

afterEach(() => { vi.unstubAllGlobals(); });
it('only classifies the confirmed duplicate-code field conflict as correctable input', () => {
  const classify = resource.actionHandlers!.createDocument.isCorrectableInputError!;
  const problem = { code: 'Document.CodeDuplicated', fieldErrors: [{ field: 'ECODE', code: 'Document.CodeDuplicated' }] };
  const duplicate = new StandardCrudRequestError('duplicate', 409, { ECODE: 'duplicate' }, problem);
  expect(classify(duplicate)).toBe(true);
  expect(duplicate.status).toBe(409);
  expect(duplicate.problem).toBe(problem);
  expect(classify(new StandardCrudRequestError('duplicate', 400, { ECODE: 'duplicate' }, problem))).toBe(false);
  expect(classify(new StandardCrudRequestError('duplicate', 409, { ECODE: 'duplicate' }, { code: 'Document.CodeDuplicated' }))).toBe(false);
  expect(classify(new StandardCrudRequestError('changed', 409, {}, { code: 'Files.UploadExpired' }))).toBe(false);
  expect(classify(new Error('transport unavailable'))).toBe(false);
});
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
it('rejects document creation without a session before sending any request', async () => {
  const request = vi.fn(async () => new Response(JSON.stringify({ requestToken: 'token', succeeded: true })));
  vi.stubGlobal('fetch', request);
  await expect(resource.actionHandlers!.createDocument({ input: { ECODE: 'D', ENAME: '图文档' },
    queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' } })).rejects.toThrow('上传会话');
  expect(request).not.toHaveBeenCalled();
});

it('reads the real folder EID for a tree action but preserves its LID row and parent scope for removal', async () => {
  const request = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(
    String(_input).endsWith('antiforgery-token') ? { requestToken: 'token' } : String(_input).includes('/folders/')
      ? { fields: { ENAME: 'Folder', Description: null } } : { succeeded: true })));
  vi.stubGlobal('fetch', request);
  const queryScope = { spaceId: 'public', rootFolderId: 'root', linkIds: '["parent-link"]', referenceObjectId: 'folder-eid' };
  const result = await resource.dataAdapter.get!('terminal-link', { queryScope, featurePosition: 'tree-edit' });
  expect(result.normalized.fields).toEqual({ ENAME: 'Folder', Description: null, LID: 'terminal-link', ObjectModelKey: 'WorkspaceFolder', ObjectId: 'folder-eid' });
  expect(String(request.mock.calls[0][0])).toBe('/api/workspace/folders/folder-eid');
  await resource.actionHandlers!.remove({ id: 'terminal-link', row: result.normalized.fields, queryScope, featurePosition: 'tree-remove' });
  const write = request.mock.calls.find(([input]) => String(input instanceof Request ? input.url : input).includes('/content/references/'))!;
  const body = write[0] instanceof Request ? await write[0].clone().json() : JSON.parse(String(write[1]?.body));
  expect(body).toEqual({ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['parent-link'] } });
  expect(write[0] instanceof Request ? write[0].method : write[1]?.method).toBe('DELETE');
  const regular = await resource.dataAdapter.get!('folder-eid', { featurePosition: 'read' });
  expect(regular.normalized.fields).toEqual({ ENAME: 'Folder', Description: null });
});

it.each(['remove', 'removeSelected'] as const)('passes the current %s declaration with the original LID and position', async handler => {
  const request = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(
    String(_input).endsWith('antiforgery-token') ? { requestToken: 'token' } : { succeeded: true })));
  vi.stubGlobal('fetch', request);
  const dataAccess = { id: 'current-remove', moduleId: 'Workspace', viewKey: 'workspace.folder.list', surface: 'rowActions',
    actionKey: handler, execution: 'workspace.clipboard.remove', resources: ['workspace.folder', 'part.part', 'document.document'],
    actions: ['read', 'edit'], featurePositions: ['current-feature'] };
  await resource.actionHandlers![handler]({ id: 'reference', selectedRows: [{ LID: 'reference', ObjectId: 'different-entity' }],
    queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '["parent-link"]' }, featurePosition: 'current-feature', action: { key: handler, labelKey: handler, dataAccess } });
  const write = request.mock.calls.find(([input]) => String(input instanceof Request ? input.url : input).includes('/content/references'))!;
  const headers = new Headers(write[0] instanceof Request ? write[0].headers : write[1]?.headers);
  expect(headers.get('X-NewP-Feature')).toBe('current-feature');
  expect(headers.get('X-NewP-Data-Declaration')).toBe('current-remove');
  const body = write[0] instanceof Request ? await write[0].clone().json() : JSON.parse(String(write[1]?.body));
  expect(body).toEqual({ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['parent-link'] },
    ...(handler === 'removeSelected' ? { linkIds: ['reference'] } : {}) });
});

it('activates folders by their exact incoming link path while other types retain details', () => {
  const target = resource.dataAdapter.getRowActivationTarget!;
  expect(target).toBeTypeOf('function');
  const row = { LID: 'child:link', ObjectModelKey: 'WorkspaceFolder', ObjectId: 'shared-eid' };
  expect(target(row, { queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '["parent-A"]' } }))
    .toEqual({ kind: 'tree-node', nodeKey: 'public:root:parent-A:child%3Alink' });
  expect(target(row, { queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '["parent-B"]' } }))
    .toEqual({ kind: 'tree-node', nodeKey: 'public:root:parent-B:child%3Alink' });
  expect(target({ ...row, ObjectModelKey: 'Part' }, {})).toBeUndefined();
  expect(target({ ...row, ObjectModelKey: 'Document' }, {})).toBeUndefined();
  expect(() => target({ ...row, LID: '' }, { queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' } })).toThrow();
});
