import { expect, it, vi } from 'vitest';
import { synchronizeTree } from '../composition/treeSynchronization';
import { TreeNodeNotFoundError, type TreeProviderBinding } from '@icp-platform/sdk';
const node = (nodeKey: string) => ({ nodeKey, nodeType: 'node', label: nodeKey, hasChildren: true });
it('refreshes both move parents once and restores the selected node using its current path', async () => {
  const children = vi.fn(async (_scope: unknown, parent: string) => parent === 'new' ? [node('d')] : []);
  const provider = { children, path: vi.fn(async () => [node('root'), node('new'), node('d')]), roots: vi.fn() } as unknown as TreeProviderBinding;
  const result = await synchronizeTree(provider, {}, { roots: [node('root')], branches: { old: [node('d')], new: [] }, selected: node('d'), expanded: [] }, [{ impact: { kind: 'located', operation: 'update', nodeKey: 'd', beforeLocation: { nodeKey: 'd', parentNodeKey: 'old' }, afterLocation: { nodeKey: 'd', parentNodeKey: 'new' } }, selectCreated: false }]);
  expect(result.selected?.nodeKey).toBe('d'); expect(result.expanded).toEqual(['root', 'new']);
  expect(children.mock.calls.map(call => call[1]).sort()).toEqual(['new', 'old', 'root']);
});
it('deletes the selected department without querying its deleted path and falls back to its parent', async () => {
  const provider = { roots: vi.fn(), children: vi.fn(async () => []), path: vi.fn(async () => [node('company')]) } as unknown as TreeProviderBinding;
  const result = await synchronizeTree(provider, {}, { roots: [node('company')], branches: { company: [node('d')], d: [] }, selected: node('d'), expanded: ['d'] }, [{ impact: { kind: 'located', operation: 'delete', nodeKey: 'd', beforeLocation: { nodeKey: 'd', parentNodeKey: 'company' } }, selectCreated: false }]);
  expect(provider.path).toHaveBeenCalledWith({}, 'company'); expect(result.selected?.nodeKey).toBe('company'); expect(result.branches.d).toBeUndefined();
});
it.each([true, false])('creates with tree-origin selection=%s while directory creation preserves its old selection', async selectCreated => {
  const provider = { roots: vi.fn(), children: vi.fn(async () => [node('new')]), path: vi.fn(async (_scope, key) => key === 'new' ? [node('root'), node('new')] : [node('root')]) } as unknown as TreeProviderBinding;
  const result = await synchronizeTree(provider, {}, { roots: [node('root')], branches: {}, selected: node('root'), expanded: [] }, [{ impact: { kind: 'located', operation: 'create', nodeKey: 'new', afterLocation: { nodeKey: 'new', parentNodeKey: 'root' } }, selectCreated }]);
  expect(result.selected?.nodeKey).toBe(selectCreated ? 'new' : 'root'); expect(provider.children).toHaveBeenCalledTimes(1);
});
it.each([{ roots: [] }, { roots: [node('remaining')] }])('deletes a company and chooses the sorted remaining root or empty state', async ({ roots }) => {
  const provider = { roots: vi.fn(async () => roots), children: vi.fn(), path: vi.fn(async (_scope, key) => [node(key)]) } as unknown as TreeProviderBinding;
  const result = await synchronizeTree(provider, {}, { roots: [node('deleted'), ...roots], branches: { deleted: [] }, selected: node('deleted'), expanded: ['deleted'] }, [{ impact: { kind: 'located', operation: 'delete', nodeKey: 'deleted', beforeLocation: { nodeKey: 'deleted', parentNodeKey: null } }, selectCreated: false }]);
  expect(result.selected?.nodeKey ?? null).toBe(roots[0]?.nodeKey ?? null); expect(provider.children).not.toHaveBeenCalled();
});
it('preserves loaded unaffected ancestors during a normal edit', async () => {
  const provider = { roots: vi.fn(), children: vi.fn(async () => [node('d')]), path: vi.fn(async () => [node('root'), node('p'), node('d')]) } as unknown as TreeProviderBinding;
  const result = await synchronizeTree(provider, {}, { roots: [node('root')], branches: { root: [node('p')], p: [node('d')] }, selected: node('d'), expanded: ['root', 'p'] }, [{ impact: { kind: 'located', operation: 'update', nodeKey: 'd', beforeLocation: { nodeKey: 'd', parentNodeKey: 'p' }, afterLocation: { nodeKey: 'd', parentNodeKey: 'p' } }, selectCreated: false }]);
  expect(provider.children).toHaveBeenCalledTimes(1); expect(provider.children).toHaveBeenCalledWith({}, 'p'); expect(result.selected?.nodeKey).toBe('d');
});
it('coalesces consecutive pending moves followed by delete without querying a deleted node', async () => {
  const provider = { roots: vi.fn(), children: vi.fn(async () => []), path: vi.fn(async () => [node('p3')]) } as unknown as TreeProviderBinding;
  const result = await synchronizeTree(provider, {}, { roots: ['p1', 'p2', 'p3'].map(node), branches: { p1: [node('d')], p2: [], p3: [], d: [] }, selected: node('d'), expanded: [] }, [
    ...[['p1', 'p2'], ['p2', 'p3']].map(([before, after]) => ({ impact: { kind: 'located' as const, operation: 'update' as const, nodeKey: 'd', beforeLocation: { nodeKey: 'd', parentNodeKey: before }, afterLocation: { nodeKey: 'd', parentNodeKey: after } }, selectCreated: false })),
    { impact: { kind: 'located', operation: 'delete', nodeKey: 'd', beforeLocation: { nodeKey: 'd', parentNodeKey: 'p3' } }, selectCreated: false },
  ]);
  expect(provider.children).toHaveBeenCalledTimes(3); expect(provider.path).toHaveBeenCalledWith({}, 'p3'); expect(result.branches.d).toBeUndefined();
});
it('unknown mapping recovers all loaded branches and current path without assuming only roots were affected', async () => {
  const provider = { roots: vi.fn(async () => [node('root')]), children: vi.fn(async () => []), path: vi.fn(async () => [node('root')]) } as unknown as TreeProviderBinding;
  await synchronizeTree(provider, {}, { roots: [node('root')], branches: { root: [], p: [] }, selected: node('root'), expanded: [] }, [{ impact: { kind: 'unknown' }, selectCreated: false }]);
  expect(provider.roots).toHaveBeenCalledTimes(1); expect(provider.children).toHaveBeenCalledTimes(2);
});

it.each([true, false])('reload preserves creation selection when visible=%s and rechecks every loaded branch', async visible => {
  const provider = { roots: vi.fn(async () => [node('root')]), children: vi.fn(async (_scope, key) => key === 'root' && visible ? [node('new')] : []),
    path: vi.fn(async (_scope, key) => { if (key === 'new' && !visible) throw new TreeNodeNotFoundError(); return key === 'new' ? [node('root'), node('new')] : [node('root')]; }) } as unknown as TreeProviderBinding;
  const result = await synchronizeTree(provider, {}, { roots: [node('root')], branches: { root: [], other: [] }, selected: node('root'), expanded: [] },
    [{ impact: { kind: 'reload', operation: 'create', nodeKey: 'new', afterLocation: { nodeKey: 'new', parentNodeKey: 'root' } }, selectCreated: true }]);
  expect(result.selected?.nodeKey).toBe(visible ? 'new' : 'root');
  expect(provider.roots).toHaveBeenCalledTimes(1); expect(provider.children).toHaveBeenCalledTimes(2);
});
