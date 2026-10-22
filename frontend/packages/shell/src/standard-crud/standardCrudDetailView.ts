import { readStandardCrudFieldValue, type StandardCrudFieldDefinition } from '@icp-platform/sdk';

export function buildStandardCrudDetailPreviewData(
  detailFields: Record<string, unknown>,
  fields: StandardCrudFieldDefinition[],
): Record<string, string> {
  if (fields.length === 0) {
    return Object.fromEntries(Object.entries(detailFields).map(([key, value]) => [key, formatStandardCrudDetailValue(value)]));
  }

  return Object.fromEntries(
    fields.map((field) => [field.key, formatStandardCrudDetailValue(readStandardCrudFieldValue(detailFields, field))]),
  );
}

export function buildStandardCrudDetailColumns(
  detailFields: Record<string, unknown>,
  fields: StandardCrudFieldDefinition[],
): Array<{ key: string; title: string; dataIndex: string }> {
  if (fields.length === 0) {
    return Object.keys(detailFields).map((key) => ({ key, title: key, dataIndex: key }));
  }

  return fields.map((field) => ({ key: field.key, title: field.labelKey, dataIndex: field.key }));
}

function formatStandardCrudDetailValue(value: unknown): string {
  if (value === null || value === undefined || value === '') {
    return '-';
  }

  if (typeof value === 'boolean') {
    return value ? '是' : '否';
  }

  return String(value);
}
