import { useEffect, useRef, useState } from 'react';
import type {
  StandardCrudSummaryBlock,
  StandardCrudListResult,
  StandardCrudDetail,
  StandardCrudMutationValues,
  StandardCrudOperationResultPresenter,
  StandardCrudOperationResultPresenterRegistry,
  StandardCrudOption,
  StandardCrudLookupRequest,
  StandardCrudLookupPage,
  StandardCrudLookupExecutionContext,
  StandardCrudLookupProvider,
  LoadStandardCrudLookupOptionsInput,
  LoadStandardCrudLookupOptionsResult,
  LoadStandardCrudLookupPageInput,
  LoadStandardCrudLookupPageResult,
  UseStandardCrudLookupRuntimeInput,
  StandardCrudLookupRuntimeState,
  StandardCrudFieldDefinition,
} from './standardCrud.types';

/** 创建模块无关的操作结果 presenter registry。 */
export function createStandardCrudOperationResultPresenterRegistry(
  presenters: StandardCrudOperationResultPresenter[],
): StandardCrudOperationResultPresenterRegistry {
  return {
    resolve: (result) => presenters.find((presenter) => presenter.supports(result)),
  };
}

const EMPTY_STANDARD_CRUD_LOOKUP_DETAIL_FIELDS: Record<string, unknown> = {};

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
    if (field.emptyAsNull && (nextValue === '' || nextValue === undefined)) {
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

/** 当前值先用稳定 ID 占位；名称仅由受控 currentValueIds 响应提供。 */
function readStandardCrudCurrentLookupOption(
  field: StandardCrudFieldDefinition,
  values: Record<string, unknown>,
  _detailFields: Record<string, unknown>,
): StandardCrudOption | null {
  const value = values[field.key];
  if (typeof value !== 'string' || value.trim().length === 0) {
    return null;
  }

  return { value, label: '正在读取当前值', disabled: true };
}

/**
 * 加载标准 CRUD lookup 选项。
 *
 * 该入口收口 provider 选择、依赖缺失短路、错误降级和当前值保留；
 * 模块与 Host 只声明 fields/providers，避免把加载流程散落到业务页面。
 */
export async function loadStandardCrudLookupOptions(
  input: LoadStandardCrudLookupOptionsInput,
  context?: StandardCrudLookupExecutionContext,
): Promise<LoadStandardCrudLookupOptionsResult> {
  const result = await loadStandardCrudLookupPage(input, context);
  return { options: result.options, error: result.error,
    ...(Object.prototype.hasOwnProperty.call(result, 'failure') ? { failure: result.failure } : {}) };
}

/** 加载一页 lookup 候选，并将 legacy 数组 Provider 规范化为无后续页的分页结果。 */
export async function loadStandardCrudLookupPage(
  input: LoadStandardCrudLookupPageInput,
  context?: StandardCrudLookupExecutionContext,
): Promise<LoadStandardCrudLookupPageResult> {
  const { field, providers, values, detailFields = {} } = input;
  const page = Math.max(1, input.page ?? 1);
  const pageSize = Math.max(1, input.pageSize ?? 20);

  if (field.kind !== 'lookup') {
    return {
      options: field.options ?? [],
      currentOptions: [],
      page,
      pageSize,
      hasMore: false,
      error: null,
    };
  }

  const current = readStandardCrudCurrentLookupOption(field, values, detailFields);
  const unavailable: StandardCrudOption | null = current ? { ...current, label: '当前值不可用', unavailable: true } : null;
  const providerKey = field.lookup?.providerKey ?? '';
  const provider = providers.find((candidate) => candidate.key === providerKey);
  if (!provider) {
    return {
      options: preserveStandardCrudCurrentOption([], unavailable),
      currentOptions: unavailable ? [unavailable] : [],
      page,
      pageSize,
      hasMore: false,
      error: `未找到 lookup provider：${providerKey}`,
    };
  }

  const dependencyValues: Record<string, unknown> = {};
  for (const dependency of field.lookup?.dependsOn ?? []) {
    const dependencyValue = values[dependency];
    if (isEmptyStandardCrudLookupDependency(dependencyValue)) {
      return {
        options: preserveStandardCrudCurrentOption([], unavailable),
        currentOptions: unavailable ? [unavailable] : [],
        page,
        pageSize,
        hasMore: false,
        error: current ? '缺少关联条件，无法验证当前值' : null,
      };
    }

    dependencyValues[dependency] = dependencyValue;
  }

  try {
    const request: StandardCrudLookupRequest = {
      fieldKey: field.key,
      keyword: input.keyword?.trim() ?? '',
      page,
      pageSize,
      currentValueIds: current ? [current.value] : [],
      values,
      dependencyValues,
    };
    const executionContext = context ? [context] as const : [];
    const loaded = await provider.load(request, ...executionContext);
    const loadedPage: StandardCrudLookupPage = Array.isArray(loaded)
      ? { options: loaded, currentOptions: [], page, pageSize, hasMore: false }
      : loaded;
    const currentOptions = mergeStandardCrudLookupOptions(
      loadedPage.currentOptions,
      unavailable ? [unavailable] : [],
    );

    return {
      options: mergeStandardCrudLookupOptions(currentOptions, loadedPage.options),
      currentOptions,
      page: loadedPage.page,
      pageSize: loadedPage.pageSize,
      hasMore: loadedPage.hasMore,
      error: null,
    };
  } catch (error) {
    return {
      options: preserveStandardCrudCurrentOption([], unavailable),
      currentOptions: unavailable ? [unavailable] : [],
      page,
      pageSize,
      hasMore: false,
      error: error instanceof Error && error.message.length > 0 ? error.message : '加载选项失败',
      failure: error,
    };
  }
}

/** 按 value 合并 lookup 选项；前序项优先，保证当前值与已加载页不会被后续重复项覆盖。 */
function mergeStandardCrudLookupOptions(
  first: StandardCrudOption[],
  second: StandardCrudOption[],
): StandardCrudOption[] {
  const merged = new Map<string, StandardCrudOption>();
  [...first, ...second].forEach((option) => {
    if (!merged.has(option.value)) merged.set(option.value, option);
  });
  return Array.from(merged.values());
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
 * @deprecated 仓库内正式 Host 已改用 Platform Query Runtime；此入口只保留 public source compatibility，
 * 删除需要独立的 versioned public-contract 决策。
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
  const inFlightLookupsRef = useRef<Array<{
    providerKey: string;
    providerLoad: StandardCrudLookupProvider['load'];
    inputKey: string;
    promise: Promise<LoadStandardCrudLookupOptionsResult>;
  }>>([]);
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
          const providerKey = field.lookup?.providerKey ?? '';
          const provider = providers.find((candidate) => candidate.key === providerKey);
          const inputKey = JSON.stringify([field, values, detailFields]);
          const existingLookup = provider && inFlightLookupsRef.current.find(
            (candidate) => candidate.providerKey === providerKey
              && candidate.providerLoad === provider.load
              && candidate.inputKey === inputKey,
          );
          let pendingLookup = existingLookup?.promise;
          if (!pendingLookup) {
            pendingLookup = loadStandardCrudLookupOptions({
              field,
              providers,
              values,
              detailFields,
            });

            if (provider) {
              const entry = { providerKey, providerLoad: provider.load, inputKey, promise: pendingLookup };
              inFlightLookupsRef.current.push(entry);

              // lookup 输入可序列化；相同输入只共享 pending 代际，结果和失败都不留存。
              const clearEntry = () => {
                inFlightLookupsRef.current = inFlightLookupsRef.current.filter((candidate) => candidate !== entry);
              };
              void pendingLookup.then(clearEntry, clearEntry);
            }
          }
          const result = await pendingLookup;
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
  const items = rawItems.map((rawItem) => {
    const mapped = mapItem(rawItem);
    if (!isStandardCrudRecord(rawItem) || !isStandardCrudRecord(mapped)) return mapped;
    // 标准响应的规范 companion 属于平台输出契约，模块行映射不能将它丢弃或重新格式化。
    const companions = Object.fromEntries(Object.entries(rawItem).filter(([field, display]) => (
      field.endsWith('_DisplayValue') && (display === null || typeof display === 'string')
    )));
    return { ...mapped, ...companions };
  });

  return {
    items,
    total: typeof record.total === 'number' ? record.total : items.length,
    page: typeof record.page === 'number' ? record.page : 1,
    pageSize: typeof record.pageSize === 'number' ? record.pageSize : items.length,
    sortBy: typeof record.sortBy === 'string' ? record.sortBy : null,
    sortDirection: typeof record.sortDirection === 'string' ? record.sortDirection : null,
  };
}
