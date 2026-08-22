import { describe, expect, it } from 'vitest';
import type { StandardCrudListQueryRequest } from '@icp-platform/sdk';
import { createStandardCrudListQueryKey } from '../standard-crud/standardCrudQueryKeys';

describe('Standard CRUD query keys', () => {
  it('canonicalizes object order and nested undefined values without leaking illegal key values', () => {
    const firstRequest = {
      sequence: [1, undefined, 2],
      pageSize: undefined,
      filters: { ignored: undefined, status: 'active' },
      pageIndex: 1,
    } as unknown as StandardCrudListQueryRequest;
    const secondRequest = {
      pageIndex: 1,
      filters: { status: 'active', ignored: undefined },
      pageSize: undefined,
      sequence: [1, undefined, 2],
    } as unknown as StandardCrudListQueryRequest;

    const firstKey = createStandardCrudListQueryKey('organization.person', firstRequest);
    const secondKey = createStandardCrudListQueryKey('organization.person', secondRequest);

    expect(JSON.stringify(firstKey)).toBe(JSON.stringify(secondKey));
    expect(firstKey).toEqual([
      'standard-crud.list',
      'organization.person',
      {
        filters: { status: 'active' },
        pageIndex: 1,
        sequence: [1, null, 2],
      },
    ]);
  });
});
