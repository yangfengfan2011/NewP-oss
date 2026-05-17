import { useEffect, useState, type Dispatch, type ReactNode, type SetStateAction } from 'react';

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

/** 标准 CRUD 字段类型只描述渲染意图，具体控件流程由 Shell 运行时按资源注册装配。 */
export type StandardCrudFieldKind = 'text' | 'boolean' | 'enum' | 'lookup' | 'date' | 'readonly';

/** 标准 CRUD 选项外形；SDK 不解释业务枚举，只提供 lookup 和 enum 的稳定值/标签契约。 */
export interface StandardCrudOption {
  value: string;
  label: string;
  disabled?: boolean;
}

/** lookup 请求上下文；依赖字段值由 Shell 收集，Provider 只接收可序列化输入。 */
export interface StandardCrudLookupRequest {
  fieldKey: string;
  keyword?: string;
  values: Record<string, unknown>;
  dependencyValues: Record<string, unknown>;
}

/** lookup Provider 只返回候选项，不拥有当前值保留、加载状态或错误展示流程。 */
export interface StandardCrudLookupProvider {
  key: string;
  load: (request: StandardCrudLookupRequest) => Promise<StandardCrudOption[]>;
}

export interface LoadStandardCrudLookupOptionsInput {
  field: StandardCrudFieldDefinition;
  providers: StandardCrudLookupProvider[];
  values: Record<string, unknown>;
  detailFields?: Record<string, unknown>;
}

export interface LoadStandardCrudLookupOptionsResult {
  options: StandardCrudOption[];
  error: string | null;
}

export interface UseStandardCrudLookupRuntimeInput {
  fields: StandardCrudFieldDefinition[];
  providers?: StandardCrudLookupProvider[];
  values: Record<string, unknown>;
  detailFields?: Record<string, unknown>;
}

export interface StandardCrudLookupRuntimeState {
  lookupOptions: Record<string, StandardCrudOption[]>;
  lookupLoading: Record<string, boolean>;
  lookupErrors: Record<string, string | null>;
}

const EMPTY_STANDARD_CRUD_LOOKUP_DETAIL_FIELDS: Record<string, unknown> = {};

/** lookup 字段声明；第二批只声明能力，触发时机和依赖刷新由 Shell 运行时负责。 */
export interface StandardCrudLookupConfig {
  providerKey: string;
  dependsOn?: string[];
  placeholder?: string;
  currentValueLabelField?: string;
}

/** 表单字段声明；元数据只表达字段是什么和允许什么，不承载提交流程。 */
export interface StandardCrudFieldDefinition {
  key: string;
  labelKey: string;
  modelField: string;
  kind: StandardCrudFieldKind;
  fallbackFields?: string[];
  required?: boolean;
  readOnly?: boolean;
  trim?: boolean;
  emptyAsNull?: boolean;
  defaultValue?: unknown;
  options?: StandardCrudOption[];
  lookup?: StandardCrudLookupConfig;
}

/** 表单分区声明；字段布局归属可静态校验，真实渲染和模式切换仍由 Shell 管理。 */
export interface StandardCrudFormSection {
  key: string;
  titleKey: string;
  fields: string[];
}

/** 标准 CRUD 表单模式；模块用它声明动作边界，Shell 用它选择运行时流程。 */
export type StandardCrudFormMode = 'create' | 'edit' | 'view';

/** 标准动作声明；这里只描述入口和权限，不描述执行顺序、回滚或弹窗流程。 */
export interface StandardCrudActionDefinition {
  key: string;
  labelKey: string;
  variant?: 'default' | 'primary' | 'danger';
  standardAction?: 'create' | 'edit' | 'refresh' | 'disable' | 'submit';
  confirmMessage?: string;
  requiredPermissions?: string[];
}

/** 标准动作上下文；Shell 提供当前资源和数据快照，模块处理器不得依赖 Shell 内部状态。 */
export interface StandardCrudActionContext<TDetail = unknown, TRow = unknown> {
  id?: string;
  detail?: TDetail;
  row?: TRow;
  refresh: () => void;
}

/** 标准动作处理器；执行流程、错误边界和刷新策略由 Shell 运行时收口。 */
export type StandardCrudActionHandler<TDetail = unknown, TRow = unknown> = (
  context: StandardCrudActionContext<TDetail, TRow>,
) => void | Promise<void>;

/** 摘要渲染上下文；模块只接收规范化摘要块和详情数据，不读取 Host 私有状态。 */
export interface StandardCrudSummaryRendererContext<TData = unknown> {
  blockKey: string;
  block: StandardCrudSummaryBlock<TData>;
  title: string;
}

/** 摘要渲染器声明；空态、异常态和装载流程由 Shell 统一处理后再调用模块渲染。 */
export type StandardCrudSummaryRenderer<TData = unknown> = (context: StandardCrudSummaryRendererContext<TData>) => ReactNode;

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
 * 第一批承接列表/详情主流程；第二批继续声明字段、表单、lookup、动作和 summary
 * renderer 的稳定扩展点。具体加载、执行、状态切换和回滚仍由 Shell 运行时收口。
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
  listPanelTitle?: string;
  permissions?: string[];
  // 第二批扩展只声明资源允许的平台化能力；lookup 加载、动作状态、summary 状态和提交流程仍由 Shell 运行时执行。
  fields?: StandardCrudFieldDefinition[];
  formSections?: StandardCrudFormSection[];
  lookupProviders?: StandardCrudLookupProvider[];
  listActions?: StandardCrudActionDefinition[];
  detailActions?: StandardCrudActionDefinition[];
  actionHandlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  summaryRenderers?: Record<string, StandardCrudSummaryRenderer>;
  buildMutationValues?: (values: Record<string, unknown>) => StandardCrudMutationValues;
  validateMutationValues?: (values: Record<string, unknown>, mode: StandardCrudFormMode) => string | null;
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

/** 构建标准 CRUD i18n key；空字典或空局部 key 不拼接多余分隔符，便于模块渐进迁移。 */
export function buildStandardCrudI18nLabel(dicKey: string, localKey: string): string {
  if (dicKey.length === 0) {
    return localKey;
  }
  if (localKey.length === 0) {
    return dicKey;
  }

  return `${dicKey}.${localKey}`;
}

/** 按 modelField 读取字段值；字段存在即保留 null 语义，只有缺失时才继续查 fallbackFields。 */
export function readStandardCrudFieldValue(
  record: Record<string, unknown>,
  field: StandardCrudFieldDefinition,
): unknown {
  const keys = [field.modelField, ...(field.fallbackFields ?? [])];

  for (const key of keys) {
    if (Object.prototype.hasOwnProperty.call(record, key)) {
      return record[key] ?? null;
    }
  }

  return null;
}

/** 规范化表单提交值；只处理纯数据转换，校验、保存和回滚流程留给 Shell 运行时。 */
export function normalizeStandardCrudSubmitValues(
  fields: StandardCrudFieldDefinition[],
  values: Record<string, unknown>,
): StandardCrudMutationValues {
  const result: StandardCrudMutationValues = {};

  for (const field of fields) {
    if (field.readOnly) {
      continue;
    }

    const hasValue = Object.prototype.hasOwnProperty.call(values, field.key);
    let nextValue = hasValue ? values[field.key] : field.defaultValue ?? null;
    if (field.trim && typeof nextValue === 'string') {
      nextValue = nextValue.trim();
    }
    if (field.emptyAsNull && nextValue === '') {
      nextValue = null;
    }

    result[field.modelField] = nextValue;
  }

  return result;
}

/** provider 未返回当前值时把当前选项放到首位，避免编辑表单因枚举分页丢失已有显示。 */
export function preserveStandardCrudCurrentOption(
  options: StandardCrudOption[],
  current?: StandardCrudOption | null,
): StandardCrudOption[] {
  if (!current?.value) {
    return options;
  }

  if (options.some((option) => option.value === current.value)) {
    return options;
  }

  return [current, ...options];
}

function isEmptyStandardCrudLookupDependency(value: unknown): boolean {
  return value == null || (typeof value === 'string' && value.trim().length === 0);
}

/** 读取 lookup 当前值；只从表单快照生成显示选项，避免 provider 分页丢失编辑态旧值。 */
function readStandardCrudCurrentLookupOption(
  field: StandardCrudFieldDefinition,
  values: Record<string, unknown>,
  detailFields: Record<string, unknown>,
): StandardCrudOption | null {
  const value = values[field.key];
  if (typeof value !== 'string' || value.trim().length === 0) {
    return null;
  }

  const labelField = field.lookup?.currentValueLabelField;
  const label = labelField && typeof detailFields[labelField] === 'string' ? detailFields[labelField] : value;

  return { value, label };
}

/**
 * 加载标准 CRUD lookup 选项。
 *
 * 该入口收口 provider 选择、依赖缺失短路、错误降级和当前值保留；
 * 模块与 Host 只声明 fields/providers，避免把加载流程散落到业务页面。
 */
export async function loadStandardCrudLookupOptions(
  input: LoadStandardCrudLookupOptionsInput,
): Promise<LoadStandardCrudLookupOptionsResult> {
  const { field, providers, values, detailFields = {} } = input;

  if (field.kind !== 'lookup') {
    return { options: field.options ?? [], error: null };
  }

  const current = readStandardCrudCurrentLookupOption(field, values, detailFields);
  const providerKey = field.lookup?.providerKey ?? '';
  const provider = providers.find((candidate) => candidate.key === providerKey);
  if (!provider) {
    return {
      options: preserveStandardCrudCurrentOption([], current),
      error: `未找到 lookup provider：${providerKey}`,
    };
  }

  const dependencyValues: Record<string, unknown> = {};
  for (const dependency of field.lookup?.dependsOn ?? []) {
    const dependencyValue = values[dependency];
    if (isEmptyStandardCrudLookupDependency(dependencyValue)) {
      return { options: [], error: null };
    }

    dependencyValues[dependency] = dependencyValue;
  }

  try {
    const options = await provider.load({
      fieldKey: field.key,
      values,
      dependencyValues,
    });

    return {
      options: preserveStandardCrudCurrentOption(options, current),
      error: null,
    };
  } catch (error) {
    return {
      options: preserveStandardCrudCurrentOption([], current),
      error: error instanceof Error && error.message.length > 0 ? error.message : '加载选项失败',
    };
  }
}

/**
 * 根据 lookup dependsOn 声明清空所有后代字段。
 *
 * 级联关系来自标准字段契约，不依赖字段复制顺序或人员模块私有字段名。
 */
export function clearStandardCrudDependentLookupValues(
  values: Record<string, unknown>,
  changedFieldKey: string,
  fields: StandardCrudFieldDefinition[],
): Record<string, unknown> {
  const nextValues = { ...values };
  const pending = [changedFieldKey];

  while (pending.length > 0) {
    const parentKey = pending.shift();
    for (const field of fields) {
      if (!parentKey || !(field.lookup?.dependsOn ?? []).includes(parentKey)) {
        continue;
      }

      if (nextValues[field.key] !== '') {
        nextValues[field.key] = '';
      }
      pending.push(field.key);
    }
  }

  return nextValues;
}

/** 应用单个字段变更并同步执行 lookup 级联清空。 */
export function applyStandardCrudLookupValueChange(
  values: Record<string, unknown>,
  changedFieldKey: string,
  value: unknown,
  fields: StandardCrudFieldDefinition[],
): Record<string, unknown> {
  return clearStandardCrudDependentLookupValues({ ...values, [changedFieldKey]: value }, changedFieldKey, fields);
}

/**
 * 标准 CRUD lookup React 运行时。
 *
 * Host 与兼容创建页共享这一入口维护 loading/error/options，业务页面只做受控表单 wiring。
 */
export function useStandardCrudLookupRuntime({
  fields,
  providers = [],
  values,
  detailFields: inputDetailFields,
}: UseStandardCrudLookupRuntimeInput): StandardCrudLookupRuntimeState {
  const [lookupOptions, setLookupOptions] = useState<Record<string, StandardCrudOption[]>>({});
  const [lookupLoading, setLookupLoading] = useState<Record<string, boolean>>({});
  const [lookupErrors, setLookupErrors] = useState<Record<string, string | null>>({});
  const detailFields = inputDetailFields ?? EMPTY_STANDARD_CRUD_LOOKUP_DETAIL_FIELDS;

  useEffect(() => {
    let cancelled = false;
    const lookupFields = fields.filter((field) => field.kind === 'lookup');
    if (lookupFields.length === 0 || providers.length === 0) {
      return () => {
        cancelled = true;
      };
    }

    async function loadLookups() {
      for (const field of lookupFields) {
        try {
          setLookupLoading((current) => ({ ...current, [field.key]: true }));
          const result = await loadStandardCrudLookupOptions({
            field,
            providers,
            values,
            detailFields,
          });
          if (!cancelled) {
            setLookupOptions((current) => ({ ...current, [field.key]: result.options }));
            setLookupErrors((current) => ({ ...current, [field.key]: result.error }));
          }
        } finally {
          if (!cancelled) {
            setLookupLoading((current) => ({ ...current, [field.key]: false }));
          }
        }
      }
    }

    void loadLookups();

    return () => {
      cancelled = true;
    };
  }, [detailFields, fields, providers, values]);

  return { lookupOptions, lookupLoading, lookupErrors };
}

/** 将标准摘要状态映射为 Shell 可直接展示的默认提示，模块仍可用 renderer 覆盖具体内容。 */
export function resolveStandardCrudSummaryMessage(block: StandardCrudSummaryBlock): string {
  if (block.status === 'notConfigured') {
    return '暂未配置';
  }
  if (block.status === 'dependencyUnavailable') {
    return '依赖不可用';
  }

  return '';
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
