// @vitest-environment jsdom

import { describe, expect, it } from 'vitest';
import { createSearchTableRequest } from '@icp-platform/ui';

describe('Pro Components adapter contract', () => {
  it('keeps platform query builder as the request source of truth', async () => {
    const request = createSearchTableRequest({
      buildQuery: (filters: { keyword: string }) => ({ keyword: filters.keyword.trim() }),
      load: async (query) => ({
        items: [{ id: '1', name: `用户-${query.keyword}` }],
        total: 1,
      }),
      mapRow: (row: { id: string; name: string }) => row,
    });

    const result = await request({ keyword: ' Alice ', current: 1, pageSize: 20 });

    expect(result.success).toBe(true);
    expect(result.total).toBe(1);
    expect(result.data[0].name).toBe('用户-Alice');
  });
});
