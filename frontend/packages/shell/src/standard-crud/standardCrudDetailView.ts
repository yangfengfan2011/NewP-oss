import { readStandardCrudFieldValue, type StandardCrudFieldDefinition } from '@icp-platform/sdk';

export function buildStandardCrudDetailFormValues(
  fieldsRecord: Record<string, unknown>,
  fields: StandardCrudFieldDefinition[],
): Record<string, unknown> {
  // 表单状态只使用字段 key，后端字段名读取和提交转换统一收口在 SDK helper。
  return Object.fromEntries(fields.map((field) => [field.key, readStandardCrudFieldValue(fieldsRecord, field)]));
}

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
