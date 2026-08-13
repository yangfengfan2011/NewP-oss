export interface StandardCrudQueryFieldSchema {
  key: string;
  labelKey?: string;
  source?: 'preset' | 'model';
  field?: string;
  preset?: string;
  operator?: string;
  dataSourceKey?: string;
  hidden?: boolean;
}

export interface StandardCrudAdvancedFilterSchema {
  enabled?: boolean;
  sections?: Array<{
    key: string;
    fields?: StandardCrudQueryFieldSchema[];
  }>;
}

export interface StandardCrudQueryDefaultSchema {
  field: string;
  value: unknown;
}

export interface StandardCrudQuerySchema {
  fields: StandardCrudQueryFieldSchema[];
  advancedFilter?: StandardCrudAdvancedFilterSchema;
  defaults?: StandardCrudQueryDefaultSchema[];
  defaultSort?: { field: string; direction: 'asc' | 'desc' };
  defaultPageSize?: number;
}

export interface StandardCrudListFieldColumnSource {
  field: string;
  sortable?: boolean;
}

export interface StandardCrudListFieldActionSource {
  visibleWhen?: { field?: string };
}

export interface StandardCrudListState {
  filters: Record<string, unknown>;
  page: number;
  pageSize: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

const STANDARD_CRUD_PAGE_SIZES = [20, 50, 100] as const;

function readPositiveInteger(value: string | null, fallback: number): number {
  if (!value || !/^\d+$/.test(value)) {
    return fallback;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function readUrlFilterValue(value: string, defaultValue: unknown): unknown {
  if (typeof defaultValue === 'boolean') {
    if (value === 'true') {
      return true;
    }
    if (value === 'false') {
      return false;
    }
    return defaultValue;
  }

  if (typeof defaultValue === 'number') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : defaultValue;
  }

  return value;
}

export function parseStandardCrudListState(
  query: StandardCrudQuerySchema,
  params: URLSearchParams,
  columns: StandardCrudListFieldColumnSource[],
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): StandardCrudListState {
  const filters = buildStandardCrudDefaultFilters(query, advancedFilter);
  for (const field of collectStandardCrudQueryFields(query, advancedFilter)) {
    const value = params.get(field.key);
    if (value !== null) {
      filters[field.key] = readUrlFilterValue(value, filters[field.key]);
    }
  }

  const defaultPageSize = STANDARD_CRUD_PAGE_SIZES.includes(query.defaultPageSize as 20 | 50 | 100)
    ? query.defaultPageSize!
    : 20;
  const requestedPageSize = readPositiveInteger(params.get('pageSize'), defaultPageSize);
  const pageSize = STANDARD_CRUD_PAGE_SIZES.includes(requestedPageSize as 20 | 50 | 100)
    ? requestedPageSize
    : defaultPageSize;
  const sortableFields = new Set(columns.filter((column) => column.sortable === true).map((column) => column.field));
  const requestedSortBy = params.get('sortBy')?.trim();
  const sortBy = requestedSortBy && sortableFields.has(requestedSortBy)
    ? requestedSortBy
    : query.defaultSort?.field && sortableFields.has(query.defaultSort.field)
      ? query.defaultSort.field
      : undefined;
  const requestedDirection = params.get('sortDirection');
  const sortDirection = requestedDirection === 'asc' || requestedDirection === 'desc'
    ? requestedDirection
    : query.defaultSort && sortBy === query.defaultSort.field
      ? query.defaultSort.direction
      : sortBy
        ? 'asc'
        : undefined;

  return {
    filters,
    page: readPositiveInteger(params.get('page'), 1),
    pageSize,
    ...(sortBy ? { sortBy } : {}),
    ...(sortDirection ? { sortDirection } : {}),
  };
}

export function serializeStandardCrudListState(
  query: StandardCrudQuerySchema,
  state: StandardCrudListState,
  currentParams: URLSearchParams = new URLSearchParams(),
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): URLSearchParams {
  const params = new URLSearchParams();
  for (const routeKey of ['drawer', 'id']) {
    const routeValue = currentParams.get(routeKey);
    if (routeValue) {
      params.set(routeKey, routeValue);
    }
  }

  const filters = buildStandardCrudListQuery(query, state.filters, advancedFilter);
  for (const [key, value] of Object.entries(filters)) {
    if (value !== null && value !== undefined) {
      params.set(key, String(value));
    }
  }
  params.set('page', String(state.page));
  params.set('pageSize', String(state.pageSize));
  if (state.sortBy && state.sortDirection) {
    params.set('sortBy', state.sortBy);
    params.set('sortDirection', state.sortDirection);
  }

  return params;
}

export function collectStandardCrudQueryFields(
  query: StandardCrudQuerySchema,
  advancedFilter: StandardCrudAdvancedFilterSchema | undefined = query.advancedFilter,
): StandardCrudQueryFieldSchema[] {
  const advancedFields = (advancedFilter?.sections ?? []).flatMap((section) => section.fields ?? []);
  return [...(query.fields ?? []), ...advancedFields];
}

export function buildStandardCrudEmptyFilters(
  query: StandardCrudQuerySchema,
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): Record<string, unknown> {
  return Object.fromEntries(collectStandardCrudQueryFields(query, advancedFilter).map((field) => [field.key, '']));
}

export function buildStandardCrudDefaultFilters(
  query: StandardCrudQuerySchema,
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): Record<string, unknown> {
  const filters = buildStandardCrudEmptyFilters(query, advancedFilter);
  for (const item of query.defaults ?? []) {
    filters[item.field] = item.value;
  }

  return filters;
}

export function buildStandardCrudListQuery(
  query: StandardCrudQuerySchema,
  filters: Record<string, unknown>,
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): Record<string, string | number | boolean | null | undefined> {
  const allowedKeys = new Set(collectStandardCrudQueryFields(query, advancedFilter).map((field) => field.key));
  const result: Record<string, string | number | boolean | null | undefined> = {};

  for (const [key, value] of Object.entries(filters)) {
    if (!allowedKeys.has(key) || value === '') {
      continue;
    }

    if (
      typeof value === 'string'
      || typeof value === 'number'
      || typeof value === 'boolean'
      || value === null
      || value === undefined
    ) {
      result[key] = value;
    }
  }

  return result;
}

export function collectStandardCrudListRequestFields(
  columns: StandardCrudListFieldColumnSource[],
  rowActions: StandardCrudListFieldActionSource[],
  idField = 'EID',
): string[] {
  const fields: string[] = [];
  const add = (field: string | undefined) => {
    const normalized = field?.trim();
    if (normalized && !fields.includes(normalized)) {
      fields.push(normalized);
    }
  };

  add(idField);
  for (const column of columns) {
    add(column.field);
  }
  for (const action of rowActions) {
    add(action.visibleWhen?.field);
  }

  return fields;
}
