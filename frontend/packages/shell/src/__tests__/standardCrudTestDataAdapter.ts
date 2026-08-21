import type { StandardCrudDataAdapter } from '@icp-platform/sdk';

/** 为不触发网络请求的 Shell 契约测试提供最小规范数据 Adapter。 */
export function createStandardCrudTestDataAdapter(
  rowId = 'row-1',
): StandardCrudDataAdapter<Record<string, unknown>> {
  return {
    list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
    get: async () => {
      const normalized = { fields: {}, summaries: {} };
      return { normalized, value: normalized };
    },
    getRowId: () => rowId,
  };
}
