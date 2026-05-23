import type { StandardCrudActionDefinition, StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { collectStandardCrudQueryFields } from '../standard-crud/standardCrudMetadataQuery';

type MetadataRecord = Record<string, unknown>;

export interface StandardCrudSchemaResolverContext {
  modelDocuments?: MetadataRecord[];
  defaultLanguageCatalog?: Record<string, string>;
}

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
  standardAction?: StandardCrudActionDefinition['standardAction'];
  variant?: StandardCrudActionDefinition['variant'];
  confirmMessageKey?: string;
  handlerKey?: string;
  requiredPermissions?: string[];
}

export interface StandardCrudRuntimeColumnSchema {
  key: string;
  labelKey: string;
  source?: 'model' | 'projection';
  field?: string;
  width?: number;
  sortable?: boolean;
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

export interface StandardCrudRuntimeQuerySchema {
  fields: StandardCrudRuntimeFieldSchema[];
  defaults: Array<{ field: string; value: unknown }>;
  defaultSort?: { field: string; direction: 'asc' | 'desc' };
  defaultPageSize?: number;
  advancedFilter?: StandardCrudAdvancedFilterSchema;
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
  query: StandardCrudRuntimeQuerySchema;
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

const builtinModelFields = ['EID', 'ENAME', 'ECODE'];
const supportedStandardActions = new Set(['create', 'view', 'edit', 'refresh', 'delete', 'disable', 'submit']);
const supportedDataSourceTypes = new Set(['enum', 'model', 'provider', 'static']);
const supportedQueryPresets = new Set(['keyword']);

function readString(value: unknown, fallback = ''): string {
  return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
}

function readArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

function readRecordArray(value: unknown): MetadataRecord[] {
  return readArray<unknown>(value).filter(
    (item): item is MetadataRecord => typeof item === 'object' && item !== null && !Array.isArray(item),
  );
}

function readRecord(value: unknown): MetadataRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as MetadataRecord) : {};
}

function readPermissions(value: unknown): string[] {
  return readArray<unknown>(value).filter((item): item is string => typeof item === 'string' && item.length > 0);
}

function readAction(value: MetadataRecord): StandardCrudRuntimeActionSchema {
  const standardAction = readString(value.standardAction);
  const variant = readString(value.variant);
  const confirmMessageKey = readString(value.confirmMessageKey);
  const handlerKey = readString(value.handlerKey);
  const requiredPermissions = readPermissions(value.requiredPermissions ?? value.permissions);

  return {
    key: readString(value.key),
    labelKey: readString(value.labelKey),
    ...(standardAction.length > 0 ? { standardAction: standardAction as StandardCrudRuntimeActionSchema['standardAction'] } : {}),
    ...(variant.length > 0 ? { variant: variant as StandardCrudRuntimeActionSchema['variant'] } : {}),
    ...(confirmMessageKey.length > 0 ? { confirmMessageKey } : {}),
    ...(handlerKey.length > 0 ? { handlerKey } : {}),
    ...(requiredPermissions.length > 0 ? { requiredPermissions } : {}),
  };
}

function findModelField(model: MetadataRecord, fieldName: string): MetadataRecord {
  return readRecordArray(model.fields).find((field) => readString(field.name) === fieldName) ?? {};
}

function resolveFieldDataSourceKey(field: StandardCrudRuntimeFieldSchema, model: MetadataRecord): string | undefined {
  if (field.dataSourceKey) {
    return field.dataSourceKey;
  }
  if (field.source !== 'model' || !field.field) {
    return undefined;
  }

  const modelField = findModelField(model, field.field);
  const dataSource = readRecord(modelField.dataSource);
  const dataSourceType = readString(dataSource.type);
  if (dataSourceType === 'provider' || dataSourceType === 'enum') {
    return readString(dataSource.key) || undefined;
  }
  if (dataSourceType === 'model') {
    const modelKey = readString(dataSource.model, readString(dataSource.key));
    return modelKey.length > 0 ? `model.${modelKey}` : undefined;
  }

  return undefined;
}

function readField(value: MetadataRecord, model: MetadataRecord = {}): StandardCrudRuntimeFieldSchema {
  const field: StandardCrudRuntimeFieldSchema = {
    key: readString(value.key),
    labelKey: readString(value.labelKey),
    source: value.source === 'model' ? 'model' : 'preset',
  };
  const sourceField = readString(value.field);
  const preset = readString(value.preset);
  const operator = readString(value.operator);
  const declaredDataSourceKey = readString(value.dataSourceKey);

  if (sourceField.length > 0) {
    field.field = sourceField;
  }
  if (preset.length > 0) {
    field.preset = preset;
  }
  if (operator.length > 0) {
    field.operator = operator;
  }
  const resolvedDataSourceKey = declaredDataSourceKey || resolveFieldDataSourceKey(field, model);
  if (resolvedDataSourceKey) {
    field.dataSourceKey = resolvedDataSourceKey;
  }

  return field;
}

function readEditorMode(value: unknown): StandardCrudListRuntimeSchema['editorMode'] {
  return value === 'page' || value === 'dialog' || value === 'drawer' ? value : 'drawer';
}

function readAdvancedFilter(value: unknown, model: MetadataRecord = {}): StandardCrudAdvancedFilterSchema {
  const record = readRecord(value);

  return {
    enabled: record.enabled === true,
    titleKey: readString(record.titleKey),
    sections: readRecordArray(record.sections)
      .map((section) => ({
        key: readString(section.key),
        titleKey: readString(section.titleKey),
        fields: readRecordArray(section.fields)
          .map((field) => readField(field, model))
          .filter((field) => field.key.length > 0),
      }))
      .filter((section) => section.key.length > 0),
  };
}

function readColumn(value: MetadataRecord): StandardCrudRuntimeColumnSchema {
  const source = readString(value.source);
  const field = readString(value.field);
  const width = typeof value.width === 'number' ? value.width : undefined;
  const sortable = typeof value.sortable === 'boolean' ? value.sortable : undefined;

  return {
    key: readString(value.key),
    labelKey: readString(value.labelKey),
    ...(source === 'model' || source === 'projection' ? { source: source as StandardCrudRuntimeColumnSchema['source'] } : {}),
    ...(field.length > 0 ? { field } : {}),
    ...(width !== undefined ? { width } : {}),
    ...(sortable !== undefined ? { sortable } : {}),
  };
}

function readQuery(value: unknown, model: MetadataRecord): StandardCrudRuntimeQuerySchema {
  const record = readRecord(value);
  const defaultSort = readRecord(record.defaultSort);
  const defaultSortField = readString(defaultSort.field);
  const defaultSortDirection = defaultSort.direction === 'desc' ? 'desc' : defaultSort.direction === 'asc' ? 'asc' : undefined;
  const defaultPageSize = typeof record.defaultPageSize === 'number' ? record.defaultPageSize : undefined;

  return {
    fields: readRecordArray(record.fields)
      .map((field) => readField(field, model))
      .filter((field) => field.key.length > 0),
    defaults: readRecordArray(record.defaults)
      .map((item) => ({
        field: readString(item.field),
        value: item.value,
      }))
      .filter((item) => item.field.length > 0),
    ...(defaultSortField.length > 0 && defaultSortDirection ? { defaultSort: { field: defaultSortField, direction: defaultSortDirection } } : {}),
    ...(defaultPageSize !== undefined ? { defaultPageSize } : {}),
  };
}

function resolveAdvancedFilterSource(view: MetadataRecord, query: MetadataRecord): unknown {
  return Object.prototype.hasOwnProperty.call(view, 'advancedFilter') ? view.advancedFilter : query.advancedFilter;
}

function readDetailSection(value: unknown): StandardCrudDetailSectionSchema | null {
  if (typeof value === 'string' && value.trim().length > 0) {
    return { key: value, titleKey: value, fields: [] };
  }

  const record = readRecord(value);
  const key = readString(record.key);
  if (key.length === 0) {
    return null;
  }

  return {
    key,
    titleKey: readString(record.titleKey, key),
    fields: readArray<unknown>(record.fields).filter((field): field is string => typeof field === 'string' && field.length > 0),
  };
}

function assertDictionaryKey(dicKey: string, localKey: string, catalog: Record<string, string> | undefined): void {
  if (!catalog || localKey.length === 0) {
    return;
  }

  const fullKey = `${dicKey}.${localKey}`;
  if (catalog[fullKey] === undefined) {
    throw new Error(`missing dictionary key "${fullKey}"`);
  }
}

function findModelDocument(modelDocuments: MetadataRecord[], modelKey: string): MetadataRecord {
  const document = modelDocuments.find((item) => item?.type === 'model' && Object.prototype.hasOwnProperty.call(item, modelKey));
  return readRecord(document?.[modelKey]);
}

function requireModelDocument(
  viewKey: string,
  modelDocuments: MetadataRecord[] | undefined,
  modelKey: string,
): MetadataRecord {
  if (!modelDocuments) {
    return {};
  }

  const model = findModelDocument(modelDocuments, modelKey);
  if (modelKey.length > 0 && Object.keys(model).length === 0) {
    throw new Error(`view "${viewKey}" references unknown model "${modelKey}"`);
  }

  return model;
}

function collectModelFields(model: MetadataRecord): Set<string> {
  if (Object.keys(model).length === 0) {
    return new Set<string>();
  }

  const names = new Set<string>(builtinModelFields);
  for (const builtinField of readArray<unknown>(model.builtinFields)) {
    if (typeof builtinField === 'string' && builtinField.length > 0) {
      names.add(builtinField);
    }
  }
  for (const field of readRecordArray(model.fields)) {
    const name = readString(field.name);
    if (name.length > 0) {
      names.add(name);
    }
  }

  return names;
}

function collectProviderKeys<TFilter, TRow, TDetail>(
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
): Set<string> {
  return new Set((registration.lookupProviders ?? []).map((provider) => provider.key));
}

function assertDataSource(fieldName: string, field: MetadataRecord, providerKeys: Set<string>): void {
  const dataSource = readRecord(field.dataSource);
  const type = readString(dataSource.type);
  if (type.length === 0) {
    return;
  }

  if (!supportedDataSourceTypes.has(type)) {
    throw new Error(`unsupported dataSource type "${type}"`);
  }

  if (type === 'provider') {
    const providerKey = readString(dataSource.key);
    if (!providerKeys.has(providerKey)) {
      throw new Error(`dataSource provider "${providerKey}" for field "${fieldName}" is not registered`);
    }
  }

  if (type === 'model') {
    const modelKey = readString(dataSource.model, readString(dataSource.key));
    const providerKey = modelKey.length > 0 ? `model.${modelKey}` : 'model.';
    if (!providerKeys.has(providerKey)) {
      throw new Error(`model dataSource provider "${providerKey}" for field "${fieldName}" is not registered`);
    }
  }
}

export function assertStandardCrudModelDataSources<TFilter, TRow, TDetail>(
  model: MetadataRecord,
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
): void {
  if (Object.keys(model).length === 0) {
    return;
  }

  const providerKeys = collectProviderKeys(registration);
  for (const field of readRecordArray(model.fields)) {
    const fieldName = readString(field.name);
    if (fieldName.length === 0) {
      continue;
    }

    assertDataSource(fieldName, field, providerKeys);
  }
}

function assertAction<TFilter, TRow, TDetail>(
  action: StandardCrudRuntimeActionSchema,
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
): void {
  if (action.standardAction && !supportedStandardActions.has(action.standardAction)) {
    throw new Error(`unsupported standard action "${action.standardAction}"`);
  }

  if (action.handlerKey && !registration.actionHandlers?.[action.handlerKey]) {
    throw new Error(`action handler "${action.handlerKey}" is not registered on resource "${registration.resourceKey}"`);
  }
}

function assertModelFieldReference(
  fieldName: string | undefined,
  modelFields: Set<string>,
  message: string,
): void {
  if (!fieldName || modelFields.size === 0) {
    return;
  }
  if (!modelFields.has(fieldName)) {
    throw new Error(message);
  }
}

function assertQuery(query: StandardCrudRuntimeQuerySchema, advancedFilter?: StandardCrudAdvancedFilterSchema): void {
  const fields = collectStandardCrudQueryFields(query, advancedFilter);
  const declaredKeys = new Set(fields.map((field) => field.key));

  for (const field of fields) {
    if (field.source === 'preset' && field.preset && !supportedQueryPresets.has(field.preset)) {
      throw new Error(`unsupported query preset "${field.preset}"`);
    }
  }

  for (const item of query.defaults) {
    if (!declaredKeys.has(item.field)) {
      throw new Error(`query default "${item.field}" does not reference a declared query field`);
    }
  }
}

function assertLocalizedFieldLabels(
  dicKey: string,
  fields: StandardCrudRuntimeFieldSchema[],
  catalog: Record<string, string> | undefined,
): void {
  for (const field of fields) {
    assertDictionaryKey(dicKey, field.labelKey, catalog);
  }
}

function assertLocalizedActions(
  dicKey: string,
  actions: StandardCrudRuntimeActionSchema[],
  catalog: Record<string, string> | undefined,
): void {
  for (const action of actions) {
    assertDictionaryKey(dicKey, action.labelKey, catalog);
    assertDictionaryKey(dicKey, action.confirmMessageKey ?? '', catalog);
  }
}

export function buildStandardCrudListSchema<TFilter, TRow, TDetail>(
  view: MetadataRecord,
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
  context?: StandardCrudSchemaResolverContext,
): StandardCrudListRuntimeSchema {
  const queryRecord = readRecord(view.query);
  const exportConfig = readRecord(view.export);
  const detailViewKey = readString(view.detailViewKey, registration.detailViewKey ?? '');
  const viewKey = readString(view.key, registration.listViewKey);
  const dicKey = readString(view.dicKey, registration.resourceKey);
  const titleKey = readString(view.titleKey, 'Title');
  const descriptionKey = readString(view.descriptionKey, 'Description');
  const explicitDescriptionKey = readString(view.descriptionKey);
  const modelKey = readString(view.model);
  const model = modelKey.length > 0 ? requireModelDocument(viewKey, context?.modelDocuments, modelKey) : {};
  const modelFields = collectModelFields(model);
  const advancedFilter = readAdvancedFilter(resolveAdvancedFilterSource(view, queryRecord), model);
  const query = readQuery(queryRecord, model);
  query.advancedFilter = advancedFilter;

  const schema: StandardCrudListRuntimeSchema = {
    viewKey,
    resourceKey: registration.resourceKey,
    apiRoute: registration.apiRoute,
    dicKey,
    titleKey,
    descriptionKey,
    editorMode: readEditorMode(view.editorMode),
    detailViewKey: detailViewKey.length > 0 ? detailViewKey : undefined,
    query,
    queryFields: query.fields,
    advancedFilter,
    columns: readRecordArray(view.columns)
      .map(readColumn)
      .filter((column) => column.key.length > 0),
    pageActions: readRecordArray(view.pageActions)
      .map(readAction)
      .filter((action) => action.key.length > 0),
    rowActions: readRecordArray(view.rowActions)
      .map(readAction)
      .filter((action) => action.key.length > 0),
    exportModes: readArray<unknown>(exportConfig.modes).filter((mode): mode is string => typeof mode === 'string'),
  };

  assertDictionaryKey(dicKey, titleKey, context?.defaultLanguageCatalog);
  assertDictionaryKey(dicKey, explicitDescriptionKey, context?.defaultLanguageCatalog);
  assertLocalizedFieldLabels(dicKey, schema.query.fields, context?.defaultLanguageCatalog);
  assertDictionaryKey(dicKey, schema.advancedFilter.titleKey, context?.defaultLanguageCatalog);
  for (const section of schema.advancedFilter.sections) {
    assertDictionaryKey(dicKey, section.titleKey, context?.defaultLanguageCatalog);
    assertLocalizedFieldLabels(dicKey, section.fields, context?.defaultLanguageCatalog);
  }
  for (const column of schema.columns) {
    assertDictionaryKey(dicKey, column.labelKey, context?.defaultLanguageCatalog);
  }
  assertLocalizedActions(dicKey, [...schema.pageActions, ...schema.rowActions], context?.defaultLanguageCatalog);

  if (Object.keys(model).length > 0) {
    for (const field of collectStandardCrudQueryFields(schema.query, schema.advancedFilter)) {
      if (field.source === 'model') {
        assertModelFieldReference(
          field.field,
          modelFields,
          `query field "${field.key}" references unknown model field "${field.field}"`,
        );
      }
    }
    for (const column of schema.columns) {
      if (column.source === 'projection' && !column.field) {
        throw new Error(`projection column "${column.key}" must declare field`);
      }
      if (column.source === 'model') {
        assertModelFieldReference(
          column.field,
          modelFields,
          `column "${column.key}" references unknown model field "${column.field}"`,
        );
      }
    }
    assertStandardCrudModelDataSources(model, registration);
  }

  for (const action of [...schema.pageActions, ...schema.rowActions]) {
    assertAction(action, registration);
  }
  assertQuery(schema.query, schema.advancedFilter);

  return schema;
}

export function buildStandardCrudDetailSchema<TFilter, TRow, TDetail>(
  view: MetadataRecord,
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
  context?: StandardCrudSchemaResolverContext,
): StandardCrudDetailRuntimeSchema {
  const viewKey = readString(view.key, registration.detailViewKey ?? '');
  const dicKey = readString(view.dicKey, registration.resourceKey);
  const titleKey = readString(view.titleKey, 'Title');
  const descriptionKey = readString(view.descriptionKey, 'Description');
  const explicitDescriptionKey = readString(view.descriptionKey);
  const modelKey = readString(view.model);
  const model = modelKey.length > 0 ? requireModelDocument(viewKey, context?.modelDocuments, modelKey) : {};
  const modelFields = collectModelFields(model);
  const sections = readArray<unknown>(view.sections)
    .map(readDetailSection)
    .filter((section): section is StandardCrudDetailSectionSchema => section !== null);
  const pageActions = readRecordArray(view.pageActions)
    .map(readAction)
    .filter((action) => action.key.length > 0);

  assertDictionaryKey(dicKey, titleKey, context?.defaultLanguageCatalog);
  assertDictionaryKey(dicKey, explicitDescriptionKey, context?.defaultLanguageCatalog);
  for (const section of sections) {
    assertDictionaryKey(dicKey, section.titleKey, context?.defaultLanguageCatalog);
    for (const fieldName of section.fields) {
      assertModelFieldReference(
        fieldName,
        modelFields,
        `detail section "${section.key}" references unknown model field "${fieldName}"`,
      );
    }
  }
  assertLocalizedActions(dicKey, pageActions, context?.defaultLanguageCatalog);
  assertStandardCrudModelDataSources(model, registration);
  for (const action of pageActions) {
    assertAction(action, registration);
  }

  return {
    viewKey,
    resourceKey: registration.resourceKey,
    apiRoute: registration.apiRoute,
    dicKey,
    titleKey,
    descriptionKey,
    sections,
    summaryBlocks: readArray<unknown>(view.summaryBlocks).filter((block): block is string => typeof block === 'string'),
    pageActions,
  };
}
