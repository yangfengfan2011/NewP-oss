import { expect, it, vi } from 'vitest';
import { createWorkspaceTreeProvider } from '../workspaceTree';

const roots = [
  { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root-public', name: '公共空间' },
  { spaceId: 'mine', spaceKind: 'Personal', rootFolderId: 'root-mine', name: '个人空间' },
];
it.each([
  ['Public', 'Workspace.PublicUnavailable'],
  ['Personal', 'Workspace.PersonalUnavailable'],
])('reports a missing %s root after ensure succeeds and retries only reads', async (missingKind, reasonKey) => {
  let unavailable = true;
  const request = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => new Response(JSON.stringify(
    init?.method === 'POST' ? { initialized: true } : unavailable ? roots.filter(space => space.spaceKind !== missingKind) : roots,
  )));
  const provider = createWorkspaceTreeProvider(request);
  const scope = { kind: 'currentUser', featurePosition: 'workspace-entry' };
  await expect(provider.roots(scope)).rejects.toMatchObject({ status: 503, reasonKey });
  await expect(provider.roots(scope)).rejects.toMatchObject({ status: 503, reasonKey });
  unavailable = false;
  expect((await provider.roots(scope)).map(node => node.nodeKey)).toEqual(['public:root-public', 'mine:root-mine']);
  expect(request.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
});

it('prepares the current account once and retries only reads after a successful ensure', async () => {
  let failRead = true;
  const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') return new Response(JSON.stringify(roots[1]));
    if (failRead) { failRead = false; throw new Error('read offline'); }
    return new Response(JSON.stringify(roots));
  });
  const provider = createWorkspaceTreeProvider(request);
  const scope = { kind: 'currentUser', featurePosition: 'workspace-entry' };
  await expect(provider.roots(scope)).rejects.toThrow('read offline');
  const nodes = await provider.roots(scope);
  expect(nodes.map(node => node.context)).toEqual(roots.map(space => ({ spaceId: space.spaceId, rootFolderId: space.rootFolderId, id: space.rootFolderId, linkIds: [] })));
  expect(request.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
  expect(nodes.map(node => node.nodeKey)).toEqual(['public:root-public', 'mine:root-mine']);
  expect(nodes.every(node => !node.hasChildren)).toBe(true);
  expect(new Headers(request.mock.calls[0][1]?.headers).get('X-NewP-Feature')).toBe('workspace-entry');
  const count = request.mock.calls.length;
  await expect(provider.search(scope, 'x', 1, 20)).rejects.toThrow();
  expect(request).toHaveBeenCalledTimes(count);
});

it('rejects a stale or forged root occurrence instead of returning another path', async () => {
  const provider = createWorkspaceTreeProvider(async () => new Response(JSON.stringify({ id: 'current-root', fields: { ENAME: '当前根' } })));
  await expect(provider.path({ kind: 'currentUser', featurePosition: 'read' }, 'mine:stale-root')).rejects.toThrow();
  await expect(provider.roots({ kind: 'global', subjectId: 'other' })).rejects.toThrow();
});

it('keeps the complete ordered occurrence chain and loads only the direct folder layer', async () => {
  const request = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/path')) return new Response(JSON.stringify({ folderIds: ['root', 'parent', 'shared'], names: ['公共空间', '父级', '共享文件夹'] }));
    expect(url.pathname).toBe('/api/workspace/content/children');
    expect(url.searchParams.get('linkIds')).toBe('["first"]');
    expect([...url.searchParams.keys()]).toEqual(['spaceId', 'rootFolderId', 'linkIds']);
    return new Response(JSON.stringify({ items: [{ linkId: 'shared-link', associatedObject: { id: 'shared' }, fields: { Name: '共享文件夹' }, hasChildren: true }] }));
  });
  const provider = createWorkspaceTreeProvider(request); const scope = { kind: 'currentUser', featurePosition: 'read' };
  const nodes = await provider.children(scope, 'space:root:first');
  expect(nodes).toEqual([{ nodeKey: 'space:root:first:shared-link', nodeType: 'folder', label: '共享文件夹', hasChildren: true,
    context: { spaceId: 'space', rootFolderId: 'root', linkIds: ['first', 'shared-link'], id: 'shared' } }]);
  const path = await provider.path(scope, nodes[0].nodeKey);
  expect(path.map(item => item.nodeKey)).toEqual(['space:root', 'space:root:first', 'space:root:first:shared-link']);
  expect(await provider.resolveChange({ resourceKey: 'workspace.folder', operation: 'create', id: '' }, scope)).toEqual({ kind: 'unknown' });
});

it('gives shared descendants distinct occurrence keys even when the Entity and terminal LID are the same', async () => {
  const request = vi.fn(async () => new Response(JSON.stringify({ items: [{ linkId: 'same-link', associatedObject: { id: 'same-child' }, fields: { Name: '共享后代' }, hasChildren: false }] })));
  const provider = createWorkspaceTreeProvider(request); const scope = { kind: 'currentUser', featurePosition: 'read' };
  const first = (await provider.children(scope, 'public:root:a:a-shared'))[0];
  const second = (await provider.children(scope, 'public:root:b:b-shared'))[0];
  expect(first.context?.id).toBe(second.context?.id);
  expect(first.nodeKey).toBe('public:root:a:a-shared:same-link');
  expect(second.nodeKey).toBe('public:root:b:b-shared:same-link');
  expect(first.nodeKey).not.toBe(second.nodeKey);
});
