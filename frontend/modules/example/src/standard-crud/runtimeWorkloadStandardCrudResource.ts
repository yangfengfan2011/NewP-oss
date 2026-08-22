import { platformFetch } from '@icp-platform/core';
import type {
  StandardCrudColumnSet,
  StandardCrudListQueryRequest,
  StandardCrudListResult,
  StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

export interface RuntimeWorkloadFilter {
  keyword?: string;
}

export interface RuntimeWorkloadRow extends Record<string, unknown> {
  id: string;
  memberName: string;
}

interface RuntimeWorkloadColumnDto {
  key: string;
  field: string;
  title: string;
  valueKind?: 'text' | 'number' | 'date' | 'datetime' | 'boolean';
  sortable?: boolean;
  hideable?: boolean;
}

interface RuntimeWorkloadColumnSetDto {
  columns: RuntimeWorkloadColumnDto[];
  contextToken: string;
}

interface RuntimeWorkloadListResultDto {
  items: RuntimeWorkloadRow[];
  total: number;
  page: number;
  pageSize: number;
}

const API_ROUTE = '/api/example/runtime-workload';
const COLUMN_CONTEXT_HEADER = 'X-Example-Column-Context';

function buildPath(path: string, values: Record<string, unknown>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  }
  const query = params.toString();
  return query ? `${path}?${query}` : path;
}

async function requestJson<T>(fetcher: typeof fetch, path: string, init: RequestInit): Promise<T> {
  const response = await fetcher(path, init);
  if (!response.ok) {
    throw new Error(`Example runtime workload request failed (${response.status})`);
  }
  return response.json() as Promise<T>;
}

/** 模块 Adapter 封装 endpoint、DTO 与 token 传输，公共 Shell 只消费标准列集和列表结果。 */
export function createRuntimeWorkloadStandardCrudResource(
  fetcher: typeof fetch = platformFetch as typeof fetch,
): StandardCrudResourceRegistration<RuntimeWorkloadFilter, RuntimeWorkloadRow> {
  return {
    resourceKey: 'example.runtime-workload',
    listViewKey: 'example.runtime-workload.list',
    columnProvider: {
      load: async ({ filters, signal }): Promise<StandardCrudColumnSet> => {
        const dto = await requestJson<RuntimeWorkloadColumnSetDto>(
          fetcher,
          buildPath(`${API_ROUTE}/columns`, { keyword: filters.keyword }),
          { method: 'GET', signal },
        );
        return {
          columns: dto.columns.map((column) => ({
            key: column.key,
            field: column.field,
            title: column.title,
            ...(column.valueKind ? { valueKind: column.valueKind } : {}),
            ...(typeof column.sortable === 'boolean' ? { sortable: column.sortable } : {}),
            ...(typeof column.hideable === 'boolean' ? { hideable: column.hideable } : {}),
          })),
          contextToken: dto.contextToken,
        };
      },
    },
    dataAdapter: {
      list: async (
        query: StandardCrudListQueryRequest = {},
        readContext,
      ): Promise<StandardCrudListResult<RuntimeWorkloadRow>> => {
        const contextToken = readContext?.columnContextToken;
        if (!contextToken) throw new Error('Runtime workload rows require a column context token');
        const dto = await requestJson<RuntimeWorkloadListResultDto>(
          fetcher,
          buildPath(`${API_ROUTE}/rows`, query),
          {
            method: 'GET',
            headers: { [COLUMN_CONTEXT_HEADER]: contextToken },
            signal: readContext?.signal,
          },
        );
        return {
          items: dto.items,
          total: dto.total,
          page: dto.page,
          pageSize: dto.pageSize,
          sortBy: query.sortBy,
          sortDirection: query.sortDirection,
        };
      },
      get: async () => {
        throw new Error('Runtime workload example does not expose a detail resource');
      },
      getRowId: (row) => row.id,
    },
  };
}

export const runtimeWorkloadStandardCrudResource = createRuntimeWorkloadStandardCrudResource();
