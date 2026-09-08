import { afterEach, expect, it, vi } from 'vitest';
import { createStandardCrudRestDataAdapter } from '@icp-platform/sdk';
import { organizationTreeProvider } from './organizationTree';
import { departmentStandardCrudResource } from './standard-crud/departmentStandardCrudResource';
vi.mock('@icp-platform/core', () => ({ platformFetch: (...args: unknown[]) => fetch(...args as Parameters<typeof fetch>) }));
afterEach(() => { vi.restoreAllMocks(); });
it('reads missing old location before deletion and hands off a canonical success without reading the deleted row', async () => {
  const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({ fields: { EID: 'd', CompanyId: 'c', ParentDepartmentId: 'p' } }))).mockResolvedValueOnce(new Response(null, { status: 204 }));
  const result = await departmentStandardCrudResource.dataAdapter.remove!('d');
  expect(result).toMatchObject({ change: { operation: 'delete', id: 'd', before: { CompanyId: 'c', ParentDepartmentId: 'p' } } });
  expect(fetcher).toHaveBeenCalledTimes(2);
  expect(fetcher.mock.calls.map(call => call[1]?.method ?? 'GET')).toEqual(['GET', 'DELETE']);
});
it('fills an incomplete update snapshot before writing and uses the persisted after fields', async () => {
  const old = { EID: 'd', CompanyId: 'c', ParentDepartmentId: 'old' };
  const after = { EID: 'd', CompanyId: 'c', ParentDepartmentId: 'stored' };
  const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({ fields: old }))).mockResolvedValueOnce(new Response(JSON.stringify({ fields: after })));
  const result = await departmentStandardCrudResource.dataAdapter.update!('d', { ParentDepartmentId: 'submitted' }, {});
  expect(result.change).toEqual({ operation: 'update', id: 'd', before: old, after });
  expect(fetcher.mock.calls.map(call => call[1]?.method)).toEqual(['GET', 'PUT']);
});
it('does not write when completing the required old snapshot fails', async () => {
  const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}', { status: 403 }));
  await expect(departmentStandardCrudResource.dataAdapter.remove!('d')).rejects.toThrow();
  expect(fetcher).toHaveBeenCalledTimes(1); expect(fetcher.mock.calls[0][1]?.method).toBe('GET');
});
it('does not read after deletion when the caller already observed the complete old fields', async () => {
  const before = { CompanyId: 'c', ParentDepartmentId: null };
  const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }));
  const result = await departmentStandardCrudResource.dataAdapter.remove!('d', before);
  expect(result).toMatchObject({ change: { before } }); expect(fetcher).toHaveBeenCalledTimes(1);
  expect(fetcher.mock.calls[0][1]?.method).toBe('DELETE');
});

it('retains write success when post-write mapping fails so the provider requests read-only recovery', async () => {
  const fetcher = vi.fn(async () => new Response(JSON.stringify({ fields: { EID: 'd' } })));
  const adapter = createStandardCrudRestDataAdapter({ apiRoute: '/resource', fetcher, mapListItem: () => { throw new Error('mapping'); }, getRowId: () => 'd' });
  const result = await adapter.create({});
  expect(result.operation).toBe('create'); expect(fetcher).toHaveBeenCalledTimes(1);
  expect(await organizationTreeProvider.resolveChange({ ...result.change!, resourceKey: 'organization.department' }, { kind: 'global' })).toEqual({ kind: 'unknown' });
});
