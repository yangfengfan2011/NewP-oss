import type {
  StandardCrudDetail,
  StandardCrudFeatureRequestContext,
  StandardCrudOperationResult,
  StandardCrudOption,
  StandardCrudLookupPage,
  StandardCrudLookupProvider,
  StandardCrudRestLookupProviderOptions,
  StandardCrudClient,
  StandardCrudRestDataAdapter,
  StandardCrudRestDataAdapterOptions,
  StandardCrudExportResponse,
} from './standardCrud.types';
import {
  isStandardCrudRecord,
  normalizeStandardCrudDetail,
  normalizeStandardCrudListResult,
} from './standardCrud';

/** 标准 CRUD 请求错误保留 HTTP 状态与字段错误，供 Shell 做冲突、鉴权和 Form.Item 反馈。 */
export class StandardCrudRequestError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly fieldErrors: Record<string, string> = {},
  ) {
    super(message);
    this.name = 'StandardCrudRequestError';
  }
}

/** 构建标准 CRUD API 路径；`fields` 等逗号分隔参数统一交给 URLSearchParams 编码，空字符串和空值不会进入查询串。 */
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
    let problem: { message?: unknown; title?: unknown; errors?: unknown; fieldErrors?: unknown } = {};
    try {
      problem = await response.clone().json() as typeof problem;
    } catch {
      problem = {};
    }
    const legacyFieldErrors = typeof problem.errors === 'object' && problem.errors !== null && !Array.isArray(problem.errors)
      ? Object.fromEntries(Object.entries(problem.errors).flatMap(([key, value]) => {
        if (typeof value === 'string') return [[key, value]];
        if (Array.isArray(value) && typeof value[0] === 'string') return [[key, value[0]]];
        return [];
      }))
      : {};
    const standardFieldErrors = Array.isArray(problem.fieldErrors)
      ? Object.fromEntries(problem.fieldErrors.flatMap((value) => {
        if (!isStandardCrudRecord(value)) return [];
        return typeof value.field === 'string' && typeof value.message === 'string'
          ? [[value.field, value.message]]
          : [];
      }))
      : {};
    const fieldErrors = { ...legacyFieldErrors, ...standardFieldErrors };
    const backendMessage = response.status === 403
      ? '当前操作已被拒绝，请核对当前权限。'
      : typeof problem.message === 'string' && problem.message.trim().length > 0
        ? problem.message
        : typeof problem.title === 'string' && problem.title.trim().length > 0
          ? problem.title
          : response.status === 401
            ? '登录状态已失效，请重新登录后重试'
            : errorMessage;
    throw new StandardCrudRequestError(backendMessage, response.status, fieldErrors);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  try {
    return await response.json() as T;
  } catch (cause) {
    if (init.method && !['GET', 'HEAD'].includes(init.method.toUpperCase())) {
      throw Object.assign(new Error('操作结果尚未确认，请先查询实际结果，再决定是否重试。'), { cause });
    }
    throw cause;
  }
}

function parseContentDispositionFileName(contentDisposition: string | null): string | undefined {
  if (!contentDisposition) {
    return undefined;
  }

  const encodedMatch = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (encodedMatch?.[1]) {
    return decodeURIComponent(encodedMatch[1]);
  }

  const fileNameMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return fileNameMatch?.[1];
}

async function requestBlob(
  fetcher: typeof fetch,
  path: string,
  init: RequestInit,
  errorMessage: string,
): Promise<StandardCrudExportResponse> {
  const response = await fetcher(path, init);
  if (!response.ok) {
    let backendMessage: string | undefined;
    try {
      const problem = await response.json() as { message?: unknown };
      backendMessage = typeof problem.message === 'string' && problem.message.trim().length > 0
        ? problem.message
        : undefined;
    } catch {
      backendMessage = undefined;
    }
    throw new Error(backendMessage ?? errorMessage);
  }

  return {
    blob: await response.blob(),
    fileName: parseContentDispositionFileName(response.headers.get('content-disposition')),
  };
}

/** 创建标准 CRUD fetch 客户端；调用方可注入 fetcher 用于测试、鉴权包装或 Host 级横切处理。 */
function featureHeaders(context?: StandardCrudFeatureRequestContext): Record<string, string> {
  return context?.featurePosition ? { 'X-NewP-Feature': context.featurePosition } : {};
}

export function createStandardCrudClient(apiRoute: string, fetcher: typeof fetch): StandardCrudClient {
  return {
    list: (query, readContext) => requestJson<unknown>(
      fetcher,
      buildStandardCrudPath(apiRoute, { ...query, ...(readContext?.queryScope ? { queryScope: JSON.stringify(readContext.queryScope) } : {}) }),
      {
        method: 'GET',
        ...(readContext?.featurePosition ? { headers: featureHeaders(readContext) } : {}),
        ...(readContext?.signal ? { signal: readContext.signal } : {}),
      },
      '加载标准列表失败',
    ),
    exportList: (request, context) =>
      requestBlob(
        fetcher,
        `${apiRoute}/export`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', ...featureHeaders(context) },
          body: JSON.stringify(request),
        },
        '导出标准列表失败',
      ),
    getDetail: async (id, readContext) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          `${apiRoute}/${encodeURIComponent(id)}`,
          {
            method: 'GET',
        ...(readContext?.featurePosition ? { headers: featureHeaders(readContext) } : {}),
            ...(readContext?.signal ? { signal: readContext.signal } : {}),
          },
          '加载标准详情失败',
        ),
      ),
    create: async (values, context) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          apiRoute,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', ...featureHeaders(context) },
            body: JSON.stringify(values),
          },
          '创建标准资源失败',
        ),
      ),
    update: async (id, values, context) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          `${apiRoute}/${encodeURIComponent(id)}`,
          {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json', ...featureHeaders(context) },
            body: JSON.stringify(values),
          },
          '保存标准资源失败',
        ),
      ),
    delete: (id, context) =>
      requestJson<void>(
        fetcher,
        `${apiRoute}/${encodeURIComponent(id)}`,
        { method: 'DELETE', ...(context?.featurePosition ? { headers: featureHeaders(context) } : {}) },
        '删除标准资源失败',
      ),
    enable: async (id, context) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          `${apiRoute}/${encodeURIComponent(id)}/enable`,
          { method: 'POST', ...(context?.featurePosition ? { headers: featureHeaders(context) } : {}) },
          '启用标准资源失败',
        ),
      ),
    disable: async (id, context) =>
      normalizeStandardCrudDetail(
        await requestJson<unknown>(
          fetcher,
          `${apiRoute}/${encodeURIComponent(id)}/disable`,
          { method: 'POST', ...(context?.featurePosition ? { headers: featureHeaders(context) } : {}) },
          '停用标准资源失败',
        ),
      ),
  };
}

/** 把标准 REST 列表响应收敛为共享宿主唯一消费的规范分页结果。 */
export function createStandardCrudRestDataAdapter<TRow, TDetail = StandardCrudDetail>(
  options: StandardCrudRestDataAdapterOptions<TRow, TDetail>,
): StandardCrudRestDataAdapter<TRow, TDetail> {
  const client = createStandardCrudClient(options.apiRoute, options.fetcher);
  const snapshot = async (id: string, before?: Record<string, unknown>, context?: StandardCrudFeatureRequestContext) =>
    options.hasChangeSnapshot && !options.hasChangeSnapshot(before ?? {})
      ? (await client.getDetail(id, context)).fields : before;

  // HTTP 成功后的业务映射也属于同步阶段；失败保留成功事实，以 unknown 位置进行只读恢复。
  const mutationResult = (operation: 'create' | 'update', detail: StandardCrudDetail, before?: Record<string, unknown>, previousId = ''): StandardCrudOperationResult => {
    try {
      const id = options.getRowId(options.mapListItem(detail.fields));
      return { operation, payload: detail, change: { operation, id, before, after: detail.fields } };
    } catch {
      return { operation, payload: detail, change: { operation, id: previousId, before } };
    }
  };
  return {
    list: async (query, readContext) => {
      if (readContext?.columnContextToken) {
        throw new Error('Standard REST data adapter does not support a column context token');
      }
      return normalizeStandardCrudListResult(await client.list(query, readContext), options.mapListItem);
    },
    get: async (id, readContext) => {
      const normalized = await client.getDetail(id, readContext);
      return {
        normalized,
        value: options.mapDetail ? options.mapDetail(normalized) : normalized as TDetail,
      };
    },
    create: async (values, _before, context) => {
      const detail = await client.create(values, context);
      return mutationResult('create', detail);
    },
    update: async (id, values, before, context) => {
      before = await snapshot(id, before, context);
      const detail = await client.update(id, values, context);
      return mutationResult('update', detail, before, id);
    },
    remove: async (id, fields, context) => {
      const before = await snapshot(id, fields, context);
      await client.delete(id, context);
      return { operation: 'action', payload: undefined, change: { operation: 'delete', id, before } };
    },
    exportList: (request, context) => client.exportList(request, context),
    getRowId: options.getRowId,
  };
}

/** 创建消费标准 `/options` 分页契约的 lookup Provider。 */
export function createStandardCrudRestLookupProvider(
  options: StandardCrudRestLookupProviderOptions,
): StandardCrudLookupProvider {
  return {
    key: options.key,
    load: async (request, context) => {
      const search = new URLSearchParams();
      if (request.keyword.length > 0) search.set('keyword', request.keyword);
      search.set('page', String(request.page));
      search.set('pageSize', String(request.pageSize));
      if (request.currentValueIds.length > 0) {
        search.set('currentValueIds', request.currentValueIds.join(','));
      }

      const payload = await requestJson<unknown>(
        options.fetcher,
        `${options.apiRoute.replace(/\/$/, '')}/options?${search.toString()}`,
        {
          headers: { Accept: 'application/json', ...featureHeaders(context) },
          ...(context?.signal ? { signal: context.signal } : {}),
        },
        '加载标准 CRUD lookup 失败',
      );
      return normalizeStandardCrudLookupPage(payload);
    },
  };
}

/** 把后端 lookup 响应收敛为 SDK 稳定分页外形，并拒绝部分或模糊响应。 */
function normalizeStandardCrudLookupPage(value: unknown): StandardCrudLookupPage {
  if (!isStandardCrudRecord(value)
      || !Array.isArray(value.items)
      || !Array.isArray(value.currentItems)
      || typeof value.page !== 'number'
      || typeof value.pageSize !== 'number'
      || typeof value.hasMore !== 'boolean') {
    throw new Error('标准 CRUD lookup 响应不符合分页契约');
  }

  return {
    options: value.items.map(normalizeStandardCrudOption),
    currentOptions: value.currentItems.map(normalizeStandardCrudOption),
    page: value.page,
    pageSize: value.pageSize,
    hasMore: value.hasMore,
  };
}

/** 校验单个 lookup option，防止无标识或无标签项进入共享运行时。 */
function normalizeStandardCrudOption(value: unknown): StandardCrudOption {
  if (!isStandardCrudRecord(value)
      || typeof value.value !== 'string'
      || typeof value.label !== 'string'
      || (value.disabled !== undefined && typeof value.disabled !== 'boolean')) {
    throw new Error('标准 CRUD lookup 选项不符合契约');
  }

  return {
    value: value.value,
    label: value.label,
    ...(value.disabled === undefined ? {} : { disabled: value.disabled }),
  };
}
