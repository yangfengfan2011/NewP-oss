import { describe, expect, it, vi } from 'vitest';
import { platformFetch } from '@icp-platform/core';
import { TreeNodeNotFoundError } from '@icp-platform/sdk';
import { organizationPageInputs, organizationTreeProvider } from './organizationTree';
const company = { nodeKey: 'company:c1', nodeType: 'company', label: '公司', hasChildren: true, context: { id: 'c1', companyId: 'c1' } };
vi.mock('@icp-platform/core', () => ({ platformFetch: vi.fn() }));
it.each(['roots', 'children', 'path', 'search'] as const)('carries explicit scope and cancellation for %s and preserves empty/notfound/failure differences', async operation => {
  const request = (scope: Record<string, unknown>, signal: AbortSignal) => operation === 'roots' ? organizationTreeProvider.roots(scope, signal)
    : operation === 'children' ? organizationTreeProvider.children(scope, 'department:d', signal)
      : operation === 'path' ? organizationTreeProvider.path(scope, 'department:d', signal)
        : organizationTreeProvider.search(scope, '研发', 1, 20, signal);
  const controller = new AbortController();
  const fetch = vi.mocked(platformFetch); fetch.mockReset();
  expect(() => request({}, controller.signal)).toThrow(/范围/); expect(fetch).not.toHaveBeenCalled();
  const empty = operation === 'search' ? { items: [], total: 0, page: 1, pageSize: 20 } : [];
  fetch.mockResolvedValueOnce(new Response(JSON.stringify(empty)));
  expect(await request({ kind: 'global' }, controller.signal)).toEqual(empty);
  const [url, init] = fetch.mock.calls[0];
  expect(new URL(String(url), 'https://test').searchParams.get('scope')).toBe('global'); expect(init?.signal).toBe(controller.signal);
  fetch.mockResolvedValueOnce(new Response(null, { status: 404 }));
  await expect(request({ kind: 'global' }, controller.signal)).rejects.toBeInstanceOf(TreeNodeNotFoundError);
  fetch.mockResolvedValueOnce(new Response(null, { status: 500 }));
  await expect(request({ kind: 'global' }, controller.signal)).rejects.toThrow('500');
  const cancelled = new DOMException('Cancelled', 'AbortError'); fetch.mockRejectedValueOnce(cancelled);
  await expect(request({ kind: 'global' }, controller.signal)).rejects.toBe(cancelled);
});
describe('Organization page input adapters', () => {
  it('separates a company direct scope and fixed create values from user filters', () => {
    const resolver = organizationPageInputs.find(item => item.key === 'organization.company.direct')!;
    const input = resolver.resolve({ viewKey: 'organization.department.list' }, company);
    expect(input.queryScope).toEqual({ CompanyId: 'c1', ParentDepartmentId: null });
    expect(input.fixedCreateValues).toEqual({ CompanyId: 'c1', ParentDepartmentId: null });
    expect(() => resolver.resolve({}, undefined)).toThrow(/上下文/);
  });
  it('uses department identity as direct parent and rejects a company in that resolver', () => {
    const resolver = organizationPageInputs.find(item => item.key === 'organization.department.direct')!;
    const input = resolver.resolve({}, { ...company, nodeKey: 'department:d1', nodeType: 'department', context: { id: 'd1', companyId: 'c1' } });
    expect(input.queryScope).toEqual({ CompanyId: 'c1', ParentDepartmentId: 'd1' });
    expect(() => resolver.resolve({}, company)).toThrow(/上下文/);
  });
  it('distinguishes unknown before from a root position without asking the Host to infer fields', async () => {
    const scope = { kind: 'global' };
    expect(await organizationTreeProvider.resolveChange({ resourceKey: 'organization.department', operation: 'update', id: 'd1', after: { CompanyId: 'c1', ParentDepartmentId: null } }, scope)).toEqual({ kind: 'unknown' });
    expect(await organizationTreeProvider.resolveChange({ resourceKey: 'organization.department', operation: 'create', id: 'd1', after: { CompanyId: 'c1', ParentDepartmentId: null } }, scope)).toEqual({ kind: 'located', operation: 'create', nodeKey: 'department:d1', beforeLocation: undefined, afterLocation: { nodeKey: 'department:d1', parentNodeKey: 'company:c1' } });
  });
  it('keeps malformed or missing relationship fields unknown instead of inventing a company root', async () => {
    for (const parent of [undefined, 42, false]) {
      expect(await organizationTreeProvider.resolveChange({ resourceKey: 'organization.department', operation: 'delete', id: 'd', before: { CompanyId: 'c', ParentDepartmentId: parent } }, { kind: 'global' })).toEqual({ kind: 'unknown' });
    }
    expect(await organizationTreeProvider.resolveChange({ resourceKey: 'organization.person', operation: 'delete', id: 'p' }, { kind: 'global' })).toEqual({ kind: 'unrelated' });
  });

});
