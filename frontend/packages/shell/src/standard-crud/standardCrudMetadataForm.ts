import type {
  StandardCrudFieldDefinition,
  StandardCrudFormMode,
  StandardCrudFormSection,
  StandardCrudLookupProvider,
  StandardCrudOption,
} from '@icp-platform/sdk';

type MetadataRecord = Record<string, unknown>;

export interface BuildStandardCrudFormSchemaInput {
  mode: StandardCrudFormMode;
  enumOptions: Record<string, StandardCrudOption[]>;
  lookupProviders?: StandardCrudLookupProvider[];
}

export interface StandardCrudMetadataFormSchema {
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
}

const supportedUiTypes = new Set([
  'text',
  'textarea',
  'number',
  'switch',
  'checkbox',
  'select',
  'lookup',
  'date',
  'dateRange',
  'readonly',
]);

const supportedDataSourceTypes = new Set(['enum', 'model', 'provider', 'static']);

function readRecord(value: unknown): MetadataRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as MetadataRecord) : {};
}

function readArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

function readString(value: unknown, fallback = ''): string {
  return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
}

export function normalizeStandardCrudFieldKey(modelField: string): string {
  return modelField;
}

function inferFieldKind(field: MetadataRecord): StandardCrudFieldDefinition['kind'] {
  const uiType = readString(field.uiType);
  if (uiType.length > 0 && !supportedUiTypes.has(uiType)) {
    throw new Error(`unsupported uiType "${uiType}"`);
  }

  if (uiType === 'select') {
    return 'enum';
  }
  if (uiType === 'switch') {
    return 'boolean';
  }
  if (uiType.length > 0) {
    return uiType as StandardCrudFieldDefinition['kind'];
  }

  const contract = readRecord(field.contract);
  const dataSource = readRecord(field.dataSource);
  const storage = readRecord(field.storage);
  const dataSourceType = readString(dataSource.type);

  if (dataSourceType.length > 0 && !supportedDataSourceTypes.has(dataSourceType)) {
    throw new Error(`unsupported dataSource type "${dataSourceType}"`);
  }

  if (contract.valueType === 'enum' || dataSourceType === 'enum') {
    return 'enum';
  }
  if (dataSourceType === 'model' || dataSourceType === 'provider') {
    return 'lookup';
  }
  if (storage.type === 'bool') {
    return 'boolean';
  }
  if (storage.type === 'date' || storage.type === 'datetime') {
    return 'date';
  }

  return 'text';
}

function readModeConfig(field: MetadataRecord, mode: StandardCrudFormMode): MetadataRecord {
  return readRecord(readRecord(field.form)[mode]);
}

function buildLookupProviderKey(dataSource: MetadataRecord): string {
  const dataSourceType = readString(dataSource.type);
  if (dataSourceType === 'provider') {
    return readString(dataSource.key);
  }
  if (dataSourceType === 'model') {
    const modelKey = readString(dataSource.model, readString(dataSource.key));
    return modelKey.length > 0 ? `model.${modelKey}` : '';
  }

  return readString(dataSource.key);
}

function buildField(
  field: MetadataRecord,
  input: BuildStandardCrudFormSchemaInput,
): StandardCrudFieldDefinition | null {
  const modelField = readString(field.name);
  if (modelField.length === 0) {
    return null;
  }

  const modeConfig = readModeConfig(field, input.mode);
  if (modeConfig.mode === 'hidden') {
    return null;
  }

  const kind = inferFieldKind(field);
  const storage = readRecord(field.storage);
  const dataSource = readRecord(field.dataSource);
  const key = normalizeStandardCrudFieldKey(modelField);
  const result: StandardCrudFieldDefinition = {
    key,
    labelKey: `Field_${modelField}`,
    modelField,
    kind,
    required: modeConfig.required === true,
    readOnly: modeConfig.mode === 'readonly' || kind === 'readonly',
    defaultValue: Object.prototype.hasOwnProperty.call(modeConfig, 'defaultValue') ? modeConfig.defaultValue : undefined,
    trim: storage.type === 'string',
    emptyAsNull: storage.nullable === true,
  };

  if (kind === 'enum') {
    const contract = readRecord(field.contract);
    const enumKey = readString(dataSource.key, readString(contract.clrType));
    result.options = enumKey.length > 0 ? (input.enumOptions[enumKey] ?? []) : [];
  }

  if (kind === 'lookup') {
    const providerKey = buildLookupProviderKey(dataSource);
    if (providerKey.length === 0) {
      throw new Error(`lookup field "${modelField}" must declare dataSource provider`);
    }
    if (input.lookupProviders && !input.lookupProviders.some((provider) => provider.key === providerKey)) {
      throw new Error(`dataSource provider "${providerKey}" for field "${modelField}" is not registered`);
    }

    result.lookup = {
      providerKey,
      // metadata field 名就是平台协议字段名，这里不再做任何 alias 友好化映射。
      dependsOn: readArray<string>(dataSource.dependsOn).map(normalizeStandardCrudFieldKey),
      currentValueLabelField: `${modelField.replace(/Id$/, '')}Name`,
    };
  }

  return result;
}

export function buildStandardCrudFormSchemaFromModel(
  model: MetadataRecord,
  input: BuildStandardCrudFormSchemaInput,
): StandardCrudMetadataFormSchema {
  const fields = readArray<MetadataRecord>(model.fields)
    .map((field) => buildField(field, input))
    .filter((field): field is StandardCrudFieldDefinition => field !== null);
  const fieldByKey = new Map(fields.map((field) => [field.key, field]));
  const sections = readArray<MetadataRecord>(model.sections)
    .map((section) => ({
      key: readString(section.key),
      titleKey: readString(section.titleKey),
      fields: readArray<string>(section.fields)
        .map(normalizeStandardCrudFieldKey)
        .filter((fieldKey) => fieldByKey.has(fieldKey)),
    }))
    .filter((section) => section.key.length > 0);
  const orderedFieldKeys = new Set<string>();
  const orderedFields: StandardCrudFieldDefinition[] = [];

  for (const section of sections) {
    for (const fieldKey of section.fields) {
      if (orderedFieldKeys.has(fieldKey)) {
        continue;
      }

      const field = fieldByKey.get(fieldKey);
      if (!field) {
        continue;
      }

      orderedFieldKeys.add(fieldKey);
      orderedFields.push(field);
    }
  }

  for (const field of fields) {
    if (orderedFieldKeys.has(field.key)) {
      continue;
    }

    orderedFieldKeys.add(field.key);
    orderedFields.push(field);
  }

  return { fields: orderedFields, sections };
}
