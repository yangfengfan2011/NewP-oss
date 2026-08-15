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
  dependsOn?: string[];
}

export interface StandardCrudRowActionVisibilitySchema {
  field: string;
  operator: 'equals';
  value: boolean | string | number | null;
}

export interface StandardCrudRowActionDisabledSchema {
  field: string;
  operator: 'notEmpty';
  reasonKey: string;
}

export interface StandardCrudActionDataDependencySchema {
  fields: string[];
  reasonKey: string;
}

export interface StandardCrudRuntimeActionSchema {
  key: string;
  labelKey: string;
  standardAction?: StandardCrudActionDefinition['standardAction'];
  variant?: StandardCrudActionDefinition['variant'];
  confirmMessageKey?: string;
  handlerKey?: string;
  inputKey?: string;
  requiredPermissions?: string[];
  visibleWhen?: StandardCrudRowActionVisibilitySchema;
  disabledWhen?: StandardCrudRowActionDisabledSchema;
  dataDependency?: StandardCrudActionDataDependencySchema;
}

export interface StandardCrudRuntimeColumnSchema {
  key: string;
  labelKey: string;
  source: 'model' | 'projection';
  field: string;
  width?: number;
  sortable?: boolean;
  hideable?: boolean;
  responsivePriority?: 'high' | 'medium' | 'low';
  standardAction?: 'view';
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

export type StandardCrudResolvedDetailFieldKind = 'text' | 'number' | 'boolean' | 'date' | 'enum' | 'lookup';

export interface StandardCrudResolvedDetailFieldCatalogEntry {
  key: string;
  kind: StandardCrudResolvedDetailFieldKind;
  source: 'model' | 'entity' | 'builtin';
  enumKey?: string;
}

export interface StandardCrudResolvedDetailFieldSchema {
  key: string;
  labelKey: string;
  rendererKey: string;
  rendererSource: 'standard' | 'binding';
  enumKey?: string;
}

export interface StandardCrudResolvedDetailSectionSchema {
  key: string;
  titleKey: string;
  fields: StandardCrudResolvedDetailFieldSchema[];
}

export interface StandardCrudResolvedDetailSchema {
  viewKey: string;
  resourceKey: string;
  apiRoute: string;
  dicKey: string;
  titleKey: string;
  descriptionKey: string;
  fieldCatalog: StandardCrudResolvedDetailFieldCatalogEntry[];
  sections: StandardCrudResolvedDetailSectionSchema[];
  summaryBlocks: string[];
  pageActions: StandardCrudRuntimeActionSchema[];
}

const builtinModelFields = ['EID', 'ENAME', 'ECODE'];
const supportedStandardActions = new Set(['create', 'view', 'edit', 'refresh', 'delete', 'disable', 'enable', 'submit']);
const supportedActionVariants = new Set(['default', 'primary', 'danger']);
const supportedDataSourceTypes = new Set(['enum', 'model', 'provider', 'static']);
const supportedQueryPresets = new Set(['keyword']);
const reservedStandardCrudQueryKeys = new Set(['page', 'pageindex', 'pagesize', 'sortby', 'sortdirection', 'fields']);
const entityDetailFields: Array<[string, StandardCrudResolvedDetailFieldKind]> = [
  ['EID', 'text'],
  ['CreatedBy', 'text'],
  ['CreatedAt', 'date'],
  ['UpdatedBy', 'text'],
  ['UpdatedAt', 'date'],
];
const optionalBuiltinDetailFields = new Map<string, StandardCrudResolvedDetailFieldKind>([
  ['ENAME', 'text'],
  ['ECODE', 'text'],
]);

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

function readActionVisibility(value: unknown): StandardCrudRowActionVisibilitySchema {
  const record = readRecord(value);
  const field = readString(record.field);
  const operator = readString(record.operator);

  return {
    field,
    operator: operator as StandardCrudRowActionVisibilitySchema['operator'],
    value: record.value as StandardCrudRowActionVisibilitySchema['value'],
  };
}

function readAction(value: MetadataRecord): StandardCrudRuntimeActionSchema {
  const standardAction = readString(value.standardAction);
  const variant = readString(value.variant);
  const confirmMessageKey = readString(value.confirmMessageKey);
  const handlerKey = readString(value.handlerKey);
  const inputKey = readString(value.inputKey);
  const requiredPermissions = readPermissions(value.requiredPermissions ?? value.permissions);
  const visibleWhen = Object.prototype.hasOwnProperty.call(value, 'visibleWhen')
    ? readActionVisibility(value.visibleWhen)
    : undefined;
  const disabledWhenRecord = readRecord(value.disabledWhen);
  const disabledWhen = Object.keys(disabledWhenRecord).length > 0 ? {
    field: readString(disabledWhenRecord.field),
    operator: readString(disabledWhenRecord.operator) as StandardCrudRowActionDisabledSchema['operator'],
    reasonKey: readString(disabledWhenRecord.reasonKey),
  } : undefined;
  const dataDependencyRecord = readRecord(value.dataDependency);
  const dataDependency = Object.keys(dataDependencyRecord).length > 0 ? {
    fields: readArray<unknown>(dataDependencyRecord.fields)
      .filter((item): item is string => typeof item === 'string' && item.trim().length > 0),
    reasonKey: readString(dataDependencyRecord.reasonKey),
  } : undefined;

  return {
    key: readString(value.key),
    labelKey: readString(value.labelKey),
    ...(standardAction.length > 0 ? { standardAction: standardAction as StandardCrudRuntimeActionSchema['standardAction'] } : {}),
    ...(variant.length > 0 ? { variant: variant as StandardCrudRuntimeActionSchema['variant'] } : {}),
    ...(confirmMessageKey.length > 0 ? { confirmMessageKey } : {}),
    ...(handlerKey.length > 0 ? { handlerKey } : {}),
    ...(inputKey.length > 0 ? { inputKey } : {}),
    ...(requiredPermissions.length > 0 ? { requiredPermissions } : {}),
    ...(visibleWhen ? { visibleWhen } : {}),
    ...(disabledWhen ? { disabledWhen } : {}),
    ...(dataDependency ? { dataDependency } : {}),
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
  const dependsOn = readArray<unknown>(value.dependsOn)
    .filter((item): item is string => typeof item === 'string' && item.trim().length > 0);

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
  if (dependsOn.length > 0) {
    field.dependsOn = dependsOn;
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
  const hideable = typeof value.hideable === 'boolean' ? value.hideable : undefined;
  const standardAction = value.standardAction === 'view' ? 'view' : undefined;
  const responsivePriority = value.responsivePriority === 'high'
    || value.responsivePriority === 'medium'
    || value.responsivePriority === 'low'
    ? value.responsivePriority
    : undefined;

  return {
    key: readString(value.key),
    labelKey: readString(value.labelKey),
    source: source === 'projection' ? 'projection' : 'model',
    field,
    ...(width !== undefined ? { width } : {}),
    ...(sortable !== undefined ? { sortable } : {}),
    ...(hideable !== undefined ? { hideable } : {}),
    ...(responsivePriority ? { responsivePriority } : {}),
    ...(standardAction ? { standardAction } : {}),
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

function inferResolvedDetailFieldKind(field: MetadataRecord): StandardCrudResolvedDetailFieldKind {
  const uiType = readString(field.uiType);
  const dataSource = readRecord(field.dataSource);
  const dataSourceType = readString(dataSource.type);
  const contract = readRecord(field.contract);
  const storageType = readString(readRecord(field.storage).type);

  if (uiType === 'lookup' || dataSourceType === 'model' || dataSourceType === 'provider') {
    return 'lookup';
  }
  if (uiType === 'select' || contract.valueType === 'enum' || dataSourceType === 'enum') {
    return 'enum';
  }
  if (uiType === 'switch' || uiType === 'checkbox' || storageType === 'bool') {
    return 'boolean';
  }
  if (uiType === 'number' || ['byte', 'short', 'int', 'long', 'decimal', 'double', 'float'].includes(storageType)) {
    return 'number';
  }
  if (uiType === 'date' || storageType === 'date' || storageType === 'datetime') {
    return 'date';
  }

  return 'text';
}

function buildResolvedDetailFieldCatalog(model: MetadataRecord): StandardCrudResolvedDetailFieldCatalogEntry[] {
  const catalog: StandardCrudResolvedDetailFieldCatalogEntry[] = [];
  const names = new Set<string>();
  const append = (
    key: string,
    kind: StandardCrudResolvedDetailFieldKind,
    source: StandardCrudResolvedDetailFieldCatalogEntry['source'],
    display: Pick<StandardCrudResolvedDetailFieldCatalogEntry, 'enumKey'> = {},
  ): void => {
    if (key.length > 0 && names.add(key)) {
      catalog.push({ key, kind, source, ...display });
    }
  };

  for (const field of readRecordArray(model.fields)) {
    const key = readString(field.name);
    const kind = inferResolvedDetailFieldKind(field);
    const dataSource = readRecord(field.dataSource);
    const contract = readRecord(field.contract);
    append(key, kind, 'model', {
      ...(kind === 'enum'
        ? { enumKey: readString(dataSource.key, readString(contract.clrType)) }
        : {}),
    });
  }
  if (readString(model.kind) === 'Entity') {
    for (const [key, kind] of entityDetailFields) {
      append(key, kind, 'entity');
    }
  }
  for (const value of readArray<unknown>(model.builtinFields)) {
    if (typeof value !== 'string') {
      continue;
    }
    const kind = optionalBuiltinDetailFields.get(value);
    if (kind) {
      append(value, kind, 'builtin');
    }
  }

  return catalog;
}

function resolveStrictDetailView(viewDocuments: MetadataRecord[], viewKey: string): MetadataRecord {
  const matches = viewDocuments
    .filter((document) => document.type === 'view')
    .map((document) => readRecord(document.view))
    .filter((view) => readString(view.key) === viewKey && readString(view.mode) === 'detail');

  if (matches.length === 0) {
    throw new Error(`detail view "${viewKey}" is missing`);
  }
  if (matches.length > 1) {
    throw new Error(`detail view "${viewKey}" is declared multiple times`);
  }

  return matches[0];
}

function readResolvedDetailField(value: unknown): { field: string; rendererKey?: string } {
  if (typeof value === 'string' && value.trim().length > 0) {
    return { field: value };
  }

  const record = readRecord(value);
  const field = readString(record.field);
  const rendererKey = readString(record.rendererKey);
  return { field, ...(rendererKey.length > 0 ? { rendererKey } : {}) };
}

export function resolveStandardCrudDetailSchema<TFilter, TRow, TDetail>(
  viewDocuments: MetadataRecord[],
  registration: StandardCrudResourceRegistration<TFilter, TRow, TDetail>,
  context: StandardCrudSchemaResolverContext,
): StandardCrudResolvedDetailSchema {
  const viewKey = readString(registration.detailViewKey);
  if (viewKey.length === 0) {
    throw new Error(`standard CRUD resource "${registration.resourceKey}" does not declare detailViewKey`);
  }

  const view = resolveStrictDetailView(viewDocuments, viewKey);
  const modelKey = readString(view.model);
  const model = requireModelDocument(viewKey, context.modelDocuments ?? [], modelKey);
  if (modelKey.length === 0 || Object.keys(model).length === 0) {
    throw new Error(`view "${viewKey}" references unknown model "${modelKey}"`);
  }

  const fieldCatalog = buildResolvedDetailFieldCatalog(model);
  const catalogByKey = new Map(fieldCatalog.map((field) => [field.key, field]));
  const usedFields = new Set<string>();
  const rawSections = readArray<unknown>(view.sections);
  const sections = rawSections.map((value) => {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
      throw new Error(`detail view "${viewKey}" sections must be objects`);
    }

    const section = readRecord(value);
    const sectionKey = readString(section.key);
    const titleKey = readString(section.titleKey);
    const rawFields = readArray<unknown>(section.fields);
    if (sectionKey.length === 0 || titleKey.length === 0 || rawFields.length === 0) {
      throw new Error(`detail view "${viewKey}" contains an empty detail section`);
    }

    const fields = rawFields.map((fieldValue): StandardCrudResolvedDetailFieldSchema => {
      const declaration = readResolvedDetailField(fieldValue);
      const catalogField = catalogByKey.get(declaration.field);
      if (!catalogField) {
        throw new Error(`detail section "${sectionKey}" references unknown model field "${declaration.field}"`);
      }
      if (usedFields.has(declaration.field)) {
        throw new Error(`detail view "${viewKey}" contains duplicate field "${declaration.field}"`);
      }
      usedFields.add(declaration.field);
      if (declaration.rendererKey && !registration.detailFieldRenderers?.[declaration.rendererKey]) {
        throw new Error(`detail field renderer "${declaration.rendererKey}" is not registered on resource "${registration.resourceKey}"`);
      }

      return {
        key: declaration.field,
        labelKey: `Field_${declaration.field}`,
        rendererKey: declaration.rendererKey ?? `standard.${catalogField.kind}`,
        rendererSource: declaration.rendererKey ? 'binding' : 'standard',
        ...(catalogField.enumKey ? { enumKey: catalogField.enumKey } : {}),
      };
    });

    return { key: sectionKey, titleKey, fields };
  });

  if (sections.length === 0 || usedFields.size === 0) {
    throw new Error(`detail view "${viewKey}" must contain at least one section and one field`);
  }

  const pageActions = readRecordArray(view.pageActions)
    .map(readAction)
    .filter((action) => action.key.length > 0);
  for (const action of pageActions) {
    assertAction(action, registration);
    if (action.dataDependency) {
      if (action.dataDependency.fields.length === 0 || action.dataDependency.reasonKey.length === 0) {
        throw new Error(`detail action "${action.key}" dataDependency must declare fields and reasonKey`);
      }
      for (const fieldKey of action.dataDependency.fields) {
        if (!catalogByKey.has(fieldKey)) {
          throw new Error(`detail action "${action.key}" dataDependency references unknown model field "${fieldKey}"`);
        }
      }
      assertDictionaryKey(
        readString(view.dicKey, registration.resourceKey),
        action.dataDependency.reasonKey,
        context.defaultLanguageCatalog,
      );
    }
  }

  return {
    viewKey,
    resourceKey: registration.resourceKey,
    apiRoute: registration.apiRoute,
    dicKey: readString(view.dicKey, registration.resourceKey),
    titleKey: readString(view.titleKey, 'Title'),
    descriptionKey: readString(view.descriptionKey, 'Description'),
    fieldCatalog,
    sections,
    summaryBlocks: readArray<unknown>(view.summaryBlocks).filter((block): block is string => typeof block === 'string'),
    pageActions,
  };
}

function assertRequiredMetadataKey(kind: string, key: string, localKey: string): void {
  if (localKey.length === 0) {
    throw new Error(`${kind} "${key}" must declare labelKey`);
  }
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
  const matches = modelDocuments.filter((item) => item?.type === 'model' && Object.prototype.hasOwnProperty.call(item, modelKey));
  if (matches.length > 1) {
    throw new Error(`model "${modelKey}" is declared by multiple metadata documents`);
  }
  const document = matches[0];
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
  if (action.variant && !supportedActionVariants.has(action.variant)) {
    throw new Error(`unsupported action variant "${action.variant}"`);
  }

  if (action.handlerKey && !registration.actionHandlers?.[action.handlerKey]) {
    throw new Error(`action handler "${action.handlerKey}" is not registered on resource "${registration.resourceKey}"`);
  }
  if (action.inputKey && !registration.actionInputRenderers?.[action.inputKey]) {
    throw new Error(`action input renderer "${action.inputKey}" is not registered on resource "${registration.resourceKey}"`);
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

function assertActionVisibility(action: StandardCrudRuntimeActionSchema, modelFields: Set<string>): void {
  if (!action.visibleWhen) {
    return;
  }
  if (action.visibleWhen.field.length === 0) {
    throw new Error(`row action "${action.key}" visibleWhen must declare field`);
  }
  if (action.visibleWhen.operator.length === 0) {
    throw new Error(`row action "${action.key}" visibleWhen must declare operator`);
  }
  if (action.visibleWhen.operator !== 'equals') {
    throw new Error(`row action "${action.key}" visibleWhen uses unsupported operator "${action.visibleWhen.operator}"`);
  }
  if (
    typeof action.visibleWhen.value !== 'boolean'
    && typeof action.visibleWhen.value !== 'string'
    && typeof action.visibleWhen.value !== 'number'
    && action.visibleWhen.value !== null
  ) {
    throw new Error(`row action "${action.key}" visibleWhen value must be a boolean, string, number or null`);
  }
  assertModelFieldReference(
    action.visibleWhen.field,
    modelFields,
    `row action "${action.key}" visibleWhen references unknown model field "${action.visibleWhen.field}"`,
  );
}

function assertActionDisabledState(action: StandardCrudRuntimeActionSchema, modelFields: Set<string>): void {
  if (!action.disabledWhen) {
    return;
  }
  if (action.disabledWhen.operator !== 'notEmpty') {
    throw new Error(`row action "${action.key}" disabledWhen uses unsupported operator "${action.disabledWhen.operator}"`);
  }
  if (action.disabledWhen.reasonKey.length === 0) {
    throw new Error(`row action "${action.key}" disabledWhen must declare reasonKey`);
  }
  assertModelFieldReference(
    action.disabledWhen.field,
    modelFields,
    `row action "${action.key}" disabledWhen references unknown model field "${action.disabledWhen.field}"`,
  );
}

function assertQuery(query: StandardCrudRuntimeQuerySchema, advancedFilter?: StandardCrudAdvancedFilterSchema): void {
  const fields = collectStandardCrudQueryFields(query, advancedFilter);
  const declaredKeys = new Set(fields.map((field) => field.key));

  for (const field of fields) {
    if (reservedStandardCrudQueryKeys.has(field.key.toLowerCase())) {
      throw new Error(`query field key "${field.key}" is reserved for standard CRUD requests`);
    }
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
    assertRequiredMetadataKey('query field', field.key, field.labelKey);
    assertDictionaryKey(dicKey, field.labelKey, catalog);
  }
}

function assertLocalizedActions(
  dicKey: string,
  actions: StandardCrudRuntimeActionSchema[],
  catalog: Record<string, string> | undefined,
): void {
  for (const action of actions) {
    assertRequiredMetadataKey('action', action.key, action.labelKey);
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
  assertDictionaryKey(dicKey, descriptionKey, context?.defaultLanguageCatalog);
  assertLocalizedFieldLabels(dicKey, schema.query.fields, context?.defaultLanguageCatalog);
  assertDictionaryKey(dicKey, schema.advancedFilter.titleKey, context?.defaultLanguageCatalog);
  for (const section of schema.advancedFilter.sections) {
    assertRequiredMetadataKey('advanced filter section', section.key, section.titleKey);
    assertDictionaryKey(dicKey, section.titleKey, context?.defaultLanguageCatalog);
    assertLocalizedFieldLabels(dicKey, section.fields, context?.defaultLanguageCatalog);
  }
  for (const column of schema.columns) {
    assertRequiredMetadataKey('column', column.key, column.labelKey);
    assertDictionaryKey(dicKey, column.labelKey, context?.defaultLanguageCatalog);
    if (column.source === 'projection' && column.field.length === 0) {
      throw new Error(`projection column "${column.key}" must declare field`);
    }
    if (column.source === 'model' && column.field.length === 0) {
      throw new Error(`model column "${column.key}" must declare field`);
    }
  }
  assertLocalizedActions(dicKey, [...schema.pageActions, ...schema.rowActions], context?.defaultLanguageCatalog);

  for (const action of schema.rowActions) {
    assertActionVisibility(action, modelFields);
    assertActionDisabledState(action, modelFields);
    assertDictionaryKey(dicKey, action.disabledWhen?.reasonKey ?? '', context?.defaultLanguageCatalog);
  }

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
