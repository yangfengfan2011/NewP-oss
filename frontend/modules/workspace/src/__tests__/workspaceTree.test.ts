import { expect, it, vi } from 'vitest';
import { createWorkspaceTreeProvider } from '../workspaceTree';

const roots = [
  { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root-public', name: '公共空间' },
  { spaceId: 'mine', spaceKind: 'Personal', rootFolderId: 'root-mine', name: '个人空间' },
];
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
