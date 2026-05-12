import type { Dispatch, ReactNode, SetStateAction } from 'react';

/** 标准摘要块状态；平台只解释状态语义，专属展示内容仍由模块扩展承接。 */
export type StandardCrudSummaryStatus = 'ready' | 'notConfigured' | 'dependencyUnavailable';

/** 标准 CRUD 详情摘要块，第一批用于统一状态和数据外形，具体 renderer 留到第二批固化。 */
export interface StandardCrudSummaryBlock<TData = unknown> {
  status: StandardCrudSummaryStatus;
  data: TData | null;
}

/** 标准 CRUD 列表响应的前端稳定外形，Host 依赖这些字段管理分页和排序显示。 */
export interface StandardCrudListResult<TItem> {
  items: TItem[];
  total: number;
  page: number;
  pageSize: number;
  sortBy?: string | null;
  sortDirection?: string | null;
}

/** 标准 CRUD 详情响应的最小公共契约；业务字段保持 unknown，避免 SDK 写入模块语义。 */
export interface StandardCrudDetail {
  fields: Record<string, unknown>;
  summaries: Record<string, StandardCrudSummaryBlock<unknown> | undefined>;
}

/** 标准 create/update 请求体外形，字段含义由模块资源注册和后端 descriptor 共同约束。 */
export type StandardCrudMutationValues = Record<string, unknown>;

/** 标准列表列声明；复杂单元格可通过受控 render 回调保留在模块边界内。 */
export interface StandardCrudTableColumn<TRow> {
  key: string;
  title: string;
  render?: (item: TRow) => ReactNode;
}

/** 列表扩展上下文，只暴露 Host 允许模块影响的筛选、刷新和当前行数据入口。 */
export interface StandardCrudListExtensionContext<TFilter, TRow> {
  filters: TFilter;
  setFilters: Dispatch<SetStateAction<TFilter>>;
  applyFilters: () => void;
  resetFilters: () => void;
  refresh: () => void;
  loading: boolean;
  rows: TRow[];
  total: number;
}

/** 详情扩展上下文，只暴露当前 id、映射后详情和刷新能力，避免模块依赖 Shell 内部状态。 */
export interface StandardCrudDetailExtensionContext<TDetail> {
  id: string;
  detail: TDetail;
  refresh: () => void;
  loading: boolean;
}

/**
 * 模块向 Shell 注册标准 CRUD 资源的公共契约。
 *
 * 第一批仅承接列表/详情主流程和受控扩展回调；字段渲染协议、动作协议、lookup
 * 和 summary block renderer 不在这里展开，避免把第二批能力提前固化成过宽元数据。
 */
export interface StandardCrudResourceRegistration<
  TFilter = Record<string, string>,
  TRow = Record<string, unknown>,
  TDetail = StandardCrudDetail,
> {
  resourceKey: string;
  apiRoute: string;
  listViewKey: string;
  detailViewKey?: string;
  detailRoutePath?: string;
  permissions?: string[];
  createEmptyFilters: () => TFilter;
  buildListQuery: (filters: TFilter) => Record<string, string | number | boolean | null | undefined>;
  mapListItem: (value: unknown) => TRow;
  getRowId: (row: TRow) => string;
  buildColumns: (context: StandardCrudListExtensionContext<TFilter, TRow>) => StandardCrudTableColumn<TRow>[];
  renderFilters?: (context: StandardCrudListExtensionContext<TFilter, TRow>) => ReactNode;
  renderHeaderActions?: (context: StandardCrudListExtensionContext<TFilter, TRow>) => ReactNode;
  renderRowActions?: (row: TRow, context: StandardCrudListExtensionContext<TFilter, TRow>) => ReactNode;
  mapDetail?: (detail: StandardCrudDetail) => TDetail;
  renderDetail?: (context: StandardCrudDetailExtensionContext<TDetail>) => ReactNode;
  renderDetailHeaderActions?: (context: StandardCrudDetailExtensionContext<TDetail>) => ReactNode;
}

/** 标准 CRUD 客户端最小方法集，供 Shell Host 复用统一路径、错误和详情规范化行为。 */
export interface StandardCrudClient {
  list: (query?: Record<string, string | number | boolean | null | undefined>) => Promise<unknown>;
  getDetail: (id: string) => Promise<StandardCrudDetail>;
  create: (values: StandardCrudMutationValues) => Promise<StandardCrudDetail>;
  update: (id: string, values: StandardCrudMutationValues) => Promise<StandardCrudDetail>;
  disable: (id: string) => Promise<StandardCrudDetail>;
}

/** 判断未知响应是否为可读取记录；数组被排除，避免把列表误当作详情字段。 */
export function isStandardCrudRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** 标准 CRUD 字符串读取只接受非空字符串，避免把后端空值误判为有效显示值。 */
export function readStandardCrudString(value: unknown, keys: string[], fallback = ''): string {
  const record = isStandardCrudRecord(value) ? value : {};
  for (const key of keys) {
    const candidate = record[key];
    if (typeof candidate === 'string' && candidate.trim().length > 0) {
      return candidate;
    }
  }

  return fallback;
}

/** 读取可空字符串字段；只有后端显式返回 null 时保留 null，其余缺失值统一回到 null。 */
export function readStandardCrudNullableString(value: unknown, keys: string[]): string | null {
  const record = isStandardCrudRecord(value) ? value : {};
  for (const key of keys) {
    const candidate = record[key];
    if (typeof candidate === 'string' && candidate.trim().length > 0) {
      return candidate;
    }
    if (candidate === null) {
      return null;
    }
  }

  return null;
}

/** 读取布尔字段；只接受真实 boolean，字符串枚举等转换规则留给模块 mapper。 */
export function readStandardCrudBoolean(value: unknown, keys: string[], fallback = false): boolean {
  const record = isStandardCrudRecord(value) ? value : {};
  for (const key of keys) {
    const candidate = record[key];
    if (typeof candidate === 'boolean') {
      return candidate;
    }
  }

  return fallback;
}

/** 兼容第一批前的数组摘要，同时把平台标准状态收敛成稳定 summary block。 */
export function normalizeStandardCrudSummaryBlock<TData = unknown>(
  value: unknown,
  mapData?: (data: unknown) => TData,
): StandardCrudSummaryBlock<TData> {
  if (isStandardCrudRecord(value)) {
    const status = value.status;
    if (status === 'ready' || status === 'notConfigured' || status === 'dependencyUnavailable') {
      const rawData = value.data;
      return {
        status,
        data: rawData == null ? null : mapData ? mapData(rawData) : (rawData as TData),
      };
    }

    // 显式 status 但不在平台白名单内，说明后端契约可能漂移；失败时降级为依赖不可用而不是健康态。
    if (typeof status === 'string') {
      return {
        status: 'dependencyUnavailable',
        data: null,
      };
    }
  }

  if (Array.isArray(value)) {
    return {
      status: value.length > 0 ? 'ready' : 'notConfigured',
      data: value.length > 0 ? (mapData ? mapData(value) : (value as TData)) : null,
    };
  }

  if (value == null) {
    return { status: 'notConfigured', data: null };
  }

  return {
    status: 'ready',
    data: mapData ? mapData(value) : (value as TData),
  };
}

/** 规范化详情响应；缺失 fields/summaries 时降级为空对象，让 Host 可以稳定渲染错误边界。 */
export function normalizeStandardCrudDetail(value: unknown): StandardCrudDetail {
  const record = isStandardCrudRecord(value) ? value : {};
  const fields = isStandardCrudRecord(record.fields) ? record.fields : {};
  const rawSummaries = isStandardCrudRecord(record.summaries) ? record.summaries : {};
  const summaries: StandardCrudDetail['summaries'] = {};

  for (const [key, summary] of Object.entries(rawSummaries)) {
    summaries[key] = normalizeStandardCrudSummaryBlock(summary);
  }

  return { fields, summaries };
}

/** 规范化列表响应；分页字段缺失时使用首批兼容默认值，避免模块重复写响应兜底逻辑。 */
export function normalizeStandardCrudListResult<TItem>(
  value: unknown,
  mapItem: (value: unknown) => TItem,
): StandardCrudListResult<TItem> {
  const record = isStandardCrudRecord(value) ? value : {};
  const rawItems = Array.isArray(record.items) ? record.items : [];
  const items = rawItems.map(mapItem);

  return {
    items,
    total: typeof record.total === 'number' ? record.total : items.length,
    page: typeof record.page === 'number' ? record.page : 1,
    pageSize: typeof record.pageSize === 'number' ? record.pageSize : items.length,
    sortBy: typeof record.sortBy === 'string' ? record.sortBy : null,
    sortDirection: typeof record.sortDirection === 'string' ? record.sortDirection : null,
  };
}

/** 构建标准 CRUD API 路径；空字符串和空值不会进入查询串，保持筛选重置语义稳定。 */
export function buildStandardCrudPath(
  apiRoute: string,
  query?: Record<string, string | number | boolean | null | undefined>,
): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value === null || value === undefined || value === '') {
      continue;
    }

    search.set(key, String(value));
  }

  const suffix = search.toString();
  return suffix.length > 0 ? `${apiRoute}?${suffix}` : apiRoute;
}

async function requestJson<T>(fetcher: typeof fetch, path: string, init: RequestInit, errorMessage: string): Promise<T> {
  const response = await fetcher(path, init);
  if (!response.ok) {
    throw new Error(errorMessage);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

/** 创建标准 CRUD fetch 客户端；调用方可注入 fetcher 用于测试、鉴权包装或 Host 级横切处理。 */
export function createStandardCrudClient(apiRoute: string, fetcher: typeof fetch = fetch): StandardCrudClient {
  return {
    list: (query) => requestJson<unknown>(fetcher, buildStandardCrudPath(apiRoute, query), { method: 'GET' }, '加载标准列表失败'),
    getDetail: async (id) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(fetcher, `${apiRoute}/${encodeURIComponent(id)}`, { method: 'GET' }, '加载标准详情失败'),
      ),
    create: async (values) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          apiRoute,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(values),
          },
          '创建标准资源失败',
        ),
      ),
    update: async (id, values) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          `${apiRoute}/${encodeURIComponent(id)}`,
          {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(values),
          },
          '保存标准资源失败',
        ),
      ),
    disable: async (id) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          `${apiRoute}/${encodeURIComponent(id)}/disable`,
          { method: 'POST' },
          '停用标准资源失败',
        ),
      ),
  };
}
