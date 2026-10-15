import { beforeEach, expect, it, vi } from 'vitest';
import type { TreeView } from '@icp-platform/sdk';
import { TreeNodeNotFoundError } from '@icp-platform/sdk';
import { createRelationTreeProvider } from '../composition/relationTreeProvider';
import organizationTree from '../../../../modules/platform/organization/metadata/views/OrganizationStructure.view.json';
import workspaceTree from '../../../../modules/infor-center/workspace/metadata/views/WorkspaceStructure.view.json';

const { fetchMock } = vi.hoisted(() => ({ fetchMock: vi.fn() }));
vi.mock('@icp-platform/core', () => ({ platformFetch: fetchMock }));
const view = organizationTree.view as unknown as TreeView;
const scope = { kind: 'global' };
beforeEach(() => { fetchMock.mockReset(); fetchMock.mockResolvedValue({ ok: true, json: async () => [] }); });

it.each(['roots', 'children', 'path', 'search'] as const)('preserves missing/error/cancellation for %s', async operation => {
  const provider = createRelationTreeProvider(view);
  const signal = new AbortController().signal;
  const request = () => operation === 'roots' ? provider.roots(scope, signal) : operation === 'children' ? provider.children(scope, 'department:d', signal)
    : operation === 'path' ? provider.path(scope, 'department:d', signal) : provider.search(scope, '研发', 1, 20, signal);
  fetchMock.mockResolvedValueOnce({ ok: false, status: 404 });
  await expect(request()).rejects.toBeInstanceOf(TreeNodeNotFoundError);
  fetchMock.mockResolvedValueOnce({ ok: false, status: 500 });
  await expect(request()).rejects.toMatchObject({ status: 500 });
  const cancelled = new DOMException('Cancelled', 'AbortError'); fetchMock.mockRejectedValueOnce(cancelled);
  await expect(request()).rejects.toBe(cancelled);
});

it('keeps malformed parent snapshots unknown and unrelated resources out of synchronization', async () => {
  const provider = createRelationTreeProvider(view);
  for (const parent of [undefined, 42, false]) {
    expect(await provider.resolveChange({ resourceKey: 'organization.department', operation: 'delete', id: 'd', before: { CompanyId: 'c', ParentDepartmentId: parent } }, scope)).toEqual({ kind: 'unknown' });
  }
  expect(await provider.resolveChange({ resourceKey: 'organization.department', operation: 'create', id: '' }, scope)).toEqual({ kind: 'unknown' });
  expect(await provider.resolveChange({ resourceKey: 'unrelated.resource', operation: 'delete', id: 'p' }, scope)).toEqual({ kind: 'unrelated' });
});

it('uses the same configured Provider for roots, branches, paths and paged searches', async () => {
  const provider = createRelationTreeProvider(view);
  const signal = new AbortController().signal;
  await provider.roots(scope, signal);
  await provider.children(scope, 'department:d1', signal);
  await provider.path(scope, 'department:d1', signal);
  await provider.search(scope, '研发', 2, 20, signal);
  expect(fetchMock.mock.calls.map(([url]) => new URL(url, 'http://localhost').pathname)).toEqual(
    ['roots', 'children', 'path', 'search'].map(operation => `/api/trees/organization.structure/${operation}`));
  expect(new URL(fetchMock.mock.calls[3][0], 'http://localhost').searchParams.get('page')).toBe('2');
  expect(fetchMock.mock.calls.every(([, options]) => options.signal === signal)).toBe(true);
});

it('maps creation, movement and deletion from configured parent fields without membership filters', async () => {
  const provider = createRelationTreeProvider({ ...view, relationSource: {
    ...view.relationSource!, nodes: view.relationSource!.nodes.map(item => ({ ...item, filters: [] })),
  } });
  const before = { CompanyId: 'c1', ParentDepartmentId: null };
  const after = { CompanyId: 'c1', ParentDepartmentId: 'd2' };
  const result = await provider.resolveChange({ resourceKey: 'organization.department', operation: 'update', id: 'd1', before, after }, scope);
  expect(result).toMatchObject({ kind: 'located', beforeLocation: { parentNodeKey: 'company:c1' }, afterLocation: { parentNodeKey: 'department:d2' } });
  expect(await provider.resolveChange({ resourceKey: 'organization.department', operation: 'create', id: 'd1', after: before }, scope))
    .toMatchObject({ kind: 'located', afterLocation: { parentNodeKey: 'company:c1' } });
  expect(await provider.resolveChange({ resourceKey: 'organization.department', operation: 'delete', id: 'd1', before: after }, scope))
    .toMatchObject({ kind: 'located', beforeLocation: { parentNodeKey: 'department:d2' } });
  expect(await provider.resolveChange({ resourceKey: 'organization.department', operation: 'update', id: 'd1', before: {}, after }, scope)).toEqual({ kind: 'unknown' });
});

it('rejects missing scope before network I/O and distinguishes missing nodes from read errors', async () => {
  const provider = createRelationTreeProvider(view);
  await expect(provider.roots({})).rejects.toThrow('范围');
  expect(fetchMock).not.toHaveBeenCalled();
  fetchMock.mockResolvedValueOnce({ status: 404, ok: false });
  await expect(provider.path(scope, 'missing')).rejects.toBeInstanceOf(TreeNodeNotFoundError);
  fetchMock.mockResolvedValueOnce({ status: 403, ok: false });
  await expect(provider.path(scope, 'forbidden')).rejects.toMatchObject({ status: 403 });
});

it('reconciles filtered trees through server reads rather than guessing database membership', async () => {
  const filtered = structuredClone(view);
  filtered.relationSource!.nodes[1].filters = [{ field: 'IsEnabled', operator: '=', value: true }];
  const provider = createRelationTreeProvider(filtered);
  expect(await provider.resolveChange({ resourceKey: 'organization.department', operation: 'update', id: 'd1',
    before: { CompanyId: 'c1', ParentDepartmentId: null, IsEnabled: true }, after: { CompanyId: 'c1', ParentDepartmentId: null, IsEnabled: false } }, scope)).toMatchObject({ kind: 'reload', nodeKey: 'department:d1', operation: 'update' });
});

it('reloads the real filtered organization tree while retaining declared parent locations', async () => {
  const provider = createRelationTreeProvider(view);
  expect(await provider.resolveChange({ resourceKey: 'organization.department', operation: 'update', id: 'd',
    before: { CompanyId: 'c', ParentDepartmentId: null }, after: { CompanyId: 'c', ParentDepartmentId: 'parent' } }, scope))
    .toMatchObject({ kind: 'reload', beforeLocation: { parentNodeKey: 'company:c' }, afterLocation: { parentNodeKey: 'department:parent' } });
  expect(await provider.resolveChange({ resourceKey: 'organization.person', operation: 'delete', id: 'p' }, scope))
    .toEqual({ kind: 'unknown' });
});

it('uses standard Link source reads for the real Workspace view without an entry wrapper', async () => {
  const provider = createRelationTreeProvider({ ...workspaceTree.view, providerKey: 'platform.relations' } as unknown as TreeView, 'formal-entry');
  const currentUser = { kind: 'currentUser' }; const signal = new AbortController().signal;
  const key = 'space:root:parent-link:chosen-link';
  await provider.roots(currentUser, signal); await provider.children(currentUser, key, signal);
  await provider.path(currentUser, key, signal); await provider.search(currentUser, '共享', 2, 20, signal);
  expect(fetchMock.mock.calls.map(([url]) => new URL(url, 'http://localhost').pathname)).toEqual(['roots', 'children', 'path', 'search'].map(operation => `/api/trees/workspace.structure/${operation}`));
  expect(fetchMock.mock.calls.every(([, options]) => options.headers['X-NewP-Feature'] === 'formal-entry' && options.signal === signal)).toBe(true);
  expect(new URL(fetchMock.mock.calls[2][0], 'http://localhost').searchParams.get('nodeKey')).toBe(key);
  expect(await provider.resolveChange({ resourceKey: 'workspace.content', operation: 'delete', id: 'chosen-link' }, currentUser)).toEqual({ kind: 'unknown' });
});
