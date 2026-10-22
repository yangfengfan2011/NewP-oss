import { afterEach, expect, it, vi } from 'vitest';
import resource from '../workspaceReferencesResource';
import type { ActionDataAccessRequirement } from '@icp-platform/sdk';
import compiledWorkspace from '../../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
import { workspacePageInputs } from '../workspaceTree';

afterEach(() => { vi.unstubAllGlobals(); });
it('reads the same reverse protocol with parent EID and LID identities, query options and cancellation', async () => {
  const fetcher = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({ items: [{ linkId: 'LID',
    parent: { modelKey: 'WorkspaceFolder', id: 'parent' }, child: { modelKey: 'Part', id: 'same-id' },
    associatedObject: { modelKey: 'WorkspaceFolder', id: 'parent' }, fields: { LID: 'LID', Name: '父名称', UpdatedBy: 'user', UpdatedBy_DisplayValue: '用户' } }], total: 1, page: 2, pageSize: 7 })));
  vi.stubGlobal('fetch', fetcher);
  const resolver = workspacePageInputs.find(input => input.key === 'workspace.references.object')!;
  const input = resolver.resolve({ recordId: 'same-id', anchorModelKey: 'Part' });
  const signal = new AbortController().signal;
  const dataAccess = (compiledWorkspace.declarations as ActionDataAccessRequirement[]).find(item => item.viewKey === 'workspace.references.list' && item.surface === 'read')!;
  const result = await resource.dataAdapter.list({ pageIndex: 2, pageSize: 7, fields: 'LID,Name', sortBy: 'Name', keyword: '父' }, { queryScope: input.queryScope, featurePosition: 'source-read', dataAccess, signal });
  expect(resource.dataAdapter.getRowId(result.items[0])).toBe('LID');
  expect(resource.dataAdapter.getDetailId!(result.items[0])).toBe('parent');
  expect(result.items[0].UpdatedBy_DisplayValue).toBe('用户');
  const url = new URL(String(fetcher.mock.calls[0][0]), 'http://localhost');
  expect(url.pathname).toBe('/api/links/workspace.content');
  expect(Object.fromEntries(url.searchParams)).toMatchObject({ direction: 'reverse', anchorModelKey: 'Part', anchorId: 'same-id', pageIndex: '2', pageSize: '7', sortBy: 'Name', fields: 'LID,Name', keyword: '父' });
  expect(url.searchParams.has('queryScope')).toBe(false);
  expect(fetcher.mock.calls[0][1]).toMatchObject({ signal, headers: { 'X-NewP-Feature': 'source-read', 'X-NewP-Data-Declaration': dataAccess.id } });
  expect(() => resolver.resolve({ recordId: '', anchorModelKey: 'Part' })).toThrow();
});
