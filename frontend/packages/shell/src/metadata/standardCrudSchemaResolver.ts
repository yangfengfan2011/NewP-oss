import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export interface StandardCrudRuntimeFieldSchema {
  key: string;
  labelKey: string;
  source: 'preset' | 'model';
  field?: string;
  preset?: string;
  operator?: string;
  dataSourceKey?: string;
}

export interface StandardCrudRuntimeActionSchema {
  key: string;
  labelKey: string;
}

export interface StandardCrudRuntimeColumnSchema {
  key: string;
  labelKey: string;
}

export interface StandardCrudAdvancedFilterSectionSchema {
  key: string;
  titleKey: string;
  fields: StandardCrudRuntimeFieldSchema[];
}

export interface StandardCrudAdvancedFilterSchema {
  enabled: boolean;
  titleKey: string;
  sections: StandardCrudAdvancedFilterSectionSchema[];
}

export interface StandardCrudDetailSectionSchema {
  key: string;
  titleKey: string;
  fields: string[];
}

export interface StandardCrudListRuntimeSchema {
  viewKey: string;
  resourceKey: string;
  apiRoute: string;
  dicKey: string;
  titleKey: string;
  descriptionKey: string;
  editorMode: 'drawer' | 'dialog' | 'page';
  detailViewKey?: string;
  queryFields: StandardCrudRuntimeFieldSchema[];
  advancedFilter: StandardCrudAdvancedFilterSchema;
  columns: StandardCrudRuntimeColumnSchema[];
  pageActions: StandardCrudRuntimeActionSchema[];
  rowActions: StandardCrudRuntimeActionSchema[];
  exportModes: string[];
}

export interface StandardCrudDetailRuntimeSchema {
  viewKey: string;
  resourceKey: string;
  apiRoute: string;
  dicKey: string;
  titleKey: string;
  descriptionKey: string;
  sections: StandardCrudDetailSectionSchema[];
  summaryBlocks: string[];
  pageActions: StandardCrudRuntimeActionSchema[];
}

function readString(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
}

function readArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

function readRecordArray(value: unknown): Record<string, unknown>[] {
  return readArray<unknown>(value).filter(
    (item): item is Record<string, unknown> => typeof item === 'object' && item !== null && !Array.isArray(item),
  );
}

function readRecord(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
}

function readAction(value: Record<string, unknown>): StandardCrudRuntimeActionSchema {
  return {
    key: readString(value.key, ''),
    labelKey: readString(value.labelKey, ''),
  };
}

function readField(value: Record<string, unknown>): StandardCrudRuntimeFieldSchema {
  return {
    key: readString(value.key, ''),
    labelKey: readString(value.labelKey, ''),
    source: value.source === 'model' ? 'model' : 'preset',
    field: typeof value.field === 'string' ? value.field : undefined,
    preset: typeof value.preset === 'string' ? value.preset : undefined,
    operator: typeof value.operator === 'string' ? value.operator : undefined,
    dataSourceKey: typeof value.dataSourceKey === 'string' ? value.dataSourceKey : undefined,
  };
}

function readEditorMode(value: unknown): StandardCrudListRuntimeSchema['editorMode'] {
  return value === 'page' || value === 'dialog' || value === 'drawer' ? value : 'drawer';
}

function readAdvancedFilter(value: unknown): StandardCrudAdvancedFilterSchema {
  const record = readRecord(value);

  return {
    enabled: record.enabled === true,
    titleKey: readString(record.titleKey, ''),
    sections: readRecordArray(record.sections)
      .map((section) => ({
        key: readString(section.key, ''),
        titleKey: readString(section.titleKey, ''),
        fields: readRecordArray(section.fields)
          .map(readField)
          .filter((field) => field.key.length > 0),
      }))
      .filter((section) => section.key.length > 0),
  };
}

// 详情 section 兼容旧字符串声明；对象声明只保留静态归属，不承载表单保存流程。
function readDetailSection(value: unknown): StandardCrudDetailSectionSchema | null {
  if (typeof value === 'string' && value.trim().length > 0) {
    return { key: value, titleKey: value, fields: [] };
  }

  const record = readRecord(value);
  const key = readString(record.key, '');
  if (key.length === 0) {
    return null;
  }

  return {
    key,
    titleKey: readString(record.titleKey, key),
    fields: readArray<unknown>(record.fields).filter((field): field is string => typeof field === 'string' && field.length > 0),
  };
}

export function buildStandardCrudListSchema<TFilter, TRow, TDetail>(
  view: Record<string, unknown>,
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
): StandardCrudListRuntimeSchema {
  const query = readRecord(view.query);
  const exportConfig = readRecord(view.export);
  const detailViewKey = readString(view.detailViewKey, registration.detailViewKey ?? '');

  return {
    viewKey: readString(view.key, registration.listViewKey),
    resourceKey: registration.resourceKey,
    apiRoute: registration.apiRoute,
    dicKey: readString(view.dicKey, registration.resourceKey),
    titleKey: readString(view.titleKey, 'Title'),
    descriptionKey: readString(view.descriptionKey, 'Description'),
    editorMode: readEditorMode(view.editorMode),
    detailViewKey: detailViewKey.length > 0 ? detailViewKey : undefined,
    queryFields: readRecordArray(query.fields)
      .map(readField)
      .filter((field) => field.key.length > 0),
    advancedFilter: readAdvancedFilter(view.advancedFilter),
    columns: readRecordArray(view.columns)
      .map((column) => ({
        key: readString(column.key, ''),
        labelKey: readString(column.labelKey, ''),
      }))
      .filter((column) => column.key.length > 0),
    pageActions: readRecordArray(view.pageActions)
      .map(readAction)
      .filter((action) => action.key.length > 0),
    rowActions: readRecordArray(view.rowActions)
      .map(readAction)
      .filter((action) => action.key.length > 0),
    exportModes: readArray<unknown>(exportConfig.modes).filter((mode): mode is string => typeof mode === 'string'),
  };
}

export function buildStandardCrudDetailSchema<TFilter, TRow, TDetail>(
  view: Record<string, unknown>,
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
): StandardCrudDetailRuntimeSchema {
  return {
    viewKey: readString(view.key, registration.detailViewKey ?? ''),
    resourceKey: registration.resourceKey,
    apiRoute: registration.apiRoute,
    dicKey: readString(view.dicKey, registration.resourceKey),
    titleKey: readString(view.titleKey, 'Title'),
    descriptionKey: readString(view.descriptionKey, 'Description'),
    sections: readArray<unknown>(view.sections)
      .map(readDetailSection)
      .filter((section): section is StandardCrudDetailSectionSchema => section !== null),
    summaryBlocks: readArray<unknown>(view.summaryBlocks).filter((block): block is string => typeof block === 'string'),
    pageActions: readRecordArray(view.pageActions)
      .map(readAction)
      .filter((action) => action.key.length > 0),
  };
}
