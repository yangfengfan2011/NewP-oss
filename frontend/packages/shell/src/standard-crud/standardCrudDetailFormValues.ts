import { readStandardCrudFieldValue, type StandardCrudFieldDefinition } from '@icp-platform/sdk'

export function buildStandardCrudDetailFormValues(
  fieldsRecord: Record<string, unknown>,
  fields: StandardCrudFieldDefinition[],
): Record<string, unknown> {
  // 表单状态只使用字段 key，后端字段名读取和提交转换统一收口在 SDK helper。
  return Object.fromEntries(fields.map((field) => [field.key, readStandardCrudFieldValue(fieldsRecord, field)]))
}
