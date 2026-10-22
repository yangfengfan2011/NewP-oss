import { expect, it, vi } from 'vitest';
import { createWorkspaceTreeProvider, workspacePageInputs } from '../workspaceTree';
import type { ActionDataAccessRequirement } from '@icp-platform/sdk';
import compiledWorkspace from '../../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';

const treeRead = (compiledWorkspace.declarations as ActionDataAccessRequirement[]).find(item => item.viewKey === 'workspace.structure' && item.surface === 'read');
if (!treeRead) throw new Error('正式Workspace树读取fixture缺失');
const readPosition = treeRead.featurePositions[0];
function boundProvider(fetcher: typeof fetch) {
  return createWorkspaceTreeProvider(fetcher).bindRead!({ featurePosition: readPosition, dataAccess: treeRead });
}

const spaces = [
  { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root-public', name: '公共空间' },
  { spaceId: 'mine', spaceKind: 'Personal', rootFolderId: 'root-mine', name: '个人空间' },
];
const roots = spaces.map(space => ({ nodeKey: `${space.spaceId}:${space.rootFolderId}`, nodeType: 'folder', label: space.name,
  labelKey: `Workspace.${space.spaceKind}`, hasChildren: false, contextMenuKeys: ['paste', 'clearClipboard'],
  context: { spaceId: space.spaceId, rootFolderId: space.rootFolderId, id: space.rootFolderId, linkIds: [] } }));
it('rejects an unbound migrated provider before any preparation or query request', async () => {
  const request = vi.fn(async () => new Response(JSON.stringify(roots)));
  await expect(createWorkspaceTreeProvider(request).roots({ kind: 'currentUser', featurePosition: readPosition })).rejects.toThrow('正式读取声明');
  expect(request).not.toHaveBeenCalled();
});

it('keeps roots under the existing folder feature scope while suppressing node mutation menus', async () => {
  const request = vi.fn(async () => new Response(JSON.stringify(roots)));
  const values = await boundProvider(request).roots({ kind: 'currentUser', featurePosition: readPosition });
  expect(values.map(value => [value.nodeType, value.contextMenuKeys])).toEqual([['folder', ['paste', 'clearClipboard']], ['folder', ['paste', 'clearClipboard']]]);
});

it('resolves a tree reference to its exact terminal LID and source parent occurrence', () => {
  const resolve = workspacePageInputs.find(item => item.key === 'workspace.folder.reference')!.resolve;
  const node = { nodeKey: 'public:root:first:terminal', nodeType: 'folder', label: 'Folder', hasChildren: false,
    context: { spaceId: 'public', rootFolderId: 'root', id: 'folder-eid', linkIds: ['first', 'terminal'] } };
  expect(resolve({ viewKey: 'workspace.folder.list' }, node)).toMatchObject({ recordId: 'terminal',
    queryScope: { spaceId: 'public', rootFolderId: 'root', linkIds: '["first"]', referenceObjectId: 'folder-eid' } });
  expect(() => resolve({}, { ...node, context: { ...node.context, linkIds: [] } })).toThrow('普通文件夹');
  expect(() => resolve({}, { ...node, context: { ...node.context, linkIds: ['first', ''] } })).toThrow('普通文件夹');
});
it.each([
  ['Public', 'Workspace.PublicUnavailable'],
  ['Personal', 'Workspace.PersonalUnavailable'],
])('reports a missing %s root after ensure succeeds and retries only reads', async (missingKind, reasonKey) => {
  let unavailable = true;
  const request = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => new Response(JSON.stringify(
    init?.method === 'POST' ? { initialized: true } : unavailable ? roots.filter(root => root.labelKey !== `Workspace.${missingKind}`) : roots,
  )));
  const provider = boundProvider(request);
  const scope = { kind: 'currentUser', featurePosition: readPosition };
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
  const provider = boundProvider(request);
  const scope = { kind: 'currentUser', featurePosition: readPosition };
  await expect(provider.roots(scope)).rejects.toThrow('read offline');
  const nodes = await provider.roots(scope);
  expect(nodes.map(node => node.context)).toEqual(roots.map(root => root.context));
  expect(request.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
  expect(nodes.map(node => node.nodeKey)).toEqual(['public:root-public', 'mine:root-mine']);
  expect(nodes.every(node => !node.hasChildren)).toBe(true);
  expect(new Headers(request.mock.calls[0][1]?.headers).get('X-NewP-Feature')).toBe(readPosition);
});

it('rejects a stale or forged root occurrence instead of returning another path', async () => {
  const provider = boundProvider(async () => new Response('', { status: 404 }));
  await expect(provider.path({ kind: 'currentUser', featurePosition: readPosition }, 'mine:stale-root')).rejects.toThrow();
  await expect(provider.roots({ kind: 'global', subjectId: 'other' })).rejects.toThrow();
});

it('keeps the complete ordered occurrence chain and loads only the direct folder layer', async () => {
  const request = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/path')) return new Response(JSON.stringify([{ nodeKey: 'space:root', label: '公共空间' }, { nodeKey: 'space:root:first', label: '父级' }, { nodeKey: 'space:root:first:shared-link', label: '共享文件夹' }]));
    expect(url.pathname).toBe('/api/trees/workspace.structure/children');
    expect(url.searchParams.get('nodeKey')).toBe('space:root:first');
    expect([...url.searchParams.keys()]).toEqual(['scope', 'nodeKey']);
    return new Response(JSON.stringify([{ nodeKey: 'space:root:first:shared-link', nodeType: 'folder', label: '共享文件夹', hasChildren: true, context: { spaceId: 'space', rootFolderId: 'root', linkIds: ['first', 'shared-link'], id: 'shared' } }]));
  });
  const provider = boundProvider(request); const scope = { kind: 'currentUser', featurePosition: readPosition };
  const nodes = await provider.children(scope, 'space:root:first');
  expect(nodes).toEqual([{ nodeKey: 'space:root:first:shared-link', nodeType: 'folder', label: '共享文件夹', hasChildren: true,
    context: { spaceId: 'space', rootFolderId: 'root', linkIds: ['first', 'shared-link'], id: 'shared' } }]);
  const path = await provider.path(scope, nodes[0].nodeKey);
  expect(path.map(item => item.nodeKey)).toEqual(['space:root', 'space:root:first', 'space:root:first:shared-link']);
  expect(await provider.resolveChange({ resourceKey: 'workspace.folder', operation: 'create', id: '' }, scope)).toEqual({ kind: 'unknown' });
});

it('searches all occurrences with the trusted feature and preserves every prefix in nodes and paths', async () => {
  const scope = { kind: 'currentUser', featurePosition: readPosition };
  const controller = new AbortController();
  const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost');
    expect(url.pathname).toBe('/api/trees/workspace.structure/search');
    expect(Object.fromEntries(url.searchParams)).toEqual({ scope: 'currentUser', keyword: '共享%_', page: '2', pageSize: '20' });
    expect(init?.signal).toBe(controller.signal);
    expect(new Headers(init?.headers).get('X-NewP-Feature')).toBe(readPosition);
    expect(new Headers(init?.headers).get('X-NewP-Data-Declaration')).toBe(treeRead.id);
    return new Response(JSON.stringify({ items: ['a', 'b'].map(prefix => ({
      node: { nodeKey: `public:root:${prefix}:shared-link`, nodeType: 'folder', label: '共享%_', hasChildren: false,
        context: { spaceId: 'public', rootFolderId: 'root', linkIds: [prefix, 'shared-link'], id: 'shared' } },
      path: [{ nodeKey: 'public:root', label: '公共空间', labelKey: 'Workspace.Public' }, { nodeKey: `public:root:${prefix}`, label: prefix },
        { nodeKey: `public:root:${prefix}:shared-link`, label: '共享%_' }],
    })), total: 22, page: 2, pageSize: 20 }));
  });
  const result = await boundProvider(request).search(scope, '共享%_', 2, 20, controller.signal);
  expect(result.total).toBe(22);
  expect(result.items.map(item => item.node.nodeKey)).toEqual(['public:root:a:shared-link', 'public:root:b:shared-link']);
  expect(result.items.map(item => item.path.map(part => part.nodeKey))).toEqual([
    ['public:root', 'public:root:a', 'public:root:a:shared-link'], ['public:root', 'public:root:b', 'public:root:b:shared-link'],
  ]);
  expect(result.items[0].path[0]).toMatchObject({ labelKey: 'Workspace.Public' });
  expect(result.items[0].node.context).toEqual({ spaceId: 'public', rootFolderId: 'root', linkIds: ['a', 'shared-link'], id: 'shared' });
});
