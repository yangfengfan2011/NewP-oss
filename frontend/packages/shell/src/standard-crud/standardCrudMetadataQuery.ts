export interface StandardCrudQueryFieldSchema {
  key: string;
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
  defaultPageSize?: number;
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
