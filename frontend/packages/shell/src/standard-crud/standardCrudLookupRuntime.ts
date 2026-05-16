import {
  preserveStandardCrudCurrentOption,
  type StandardCrudFieldDefinition,
  type StandardCrudLookupProvider,
  type StandardCrudOption,
} from '@icp-platform/sdk';

export interface LoadStandardCrudLookupOptionsInput {
  field: StandardCrudFieldDefinition;
  providers: StandardCrudLookupProvider[];
  values: Record<string, unknown>;
  detailFields: Record<string, unknown>;
}

export interface LoadStandardCrudLookupOptionsResult {
  options: StandardCrudOption[];
  error: string | null;
}

function isEmptyDependencyValue(value: unknown): boolean {
  return value == null || (typeof value === 'string' && value.trim().length === 0);
}

/** 读取 lookup 当前值；只从表单快照生成显示选项，避免 provider 分页丢失编辑态旧值。 */
function readCurrentOption(
  field: StandardCrudFieldDefinition,
  values: Record<string, unknown>,
  detailFields: Record<string, unknown>,
): StandardCrudOption | null {
  const value = values[field.key];
  if (typeof value !== 'string' || value.trim().length === 0) {
    return null;
  }

  const labelField = field.lookup?.currentValueLabelField;
  const label = labelField && typeof detailFields[labelField] === 'string' ? detailFields[labelField] : value;

  return { value, label };
}

/**
 * 加载标准 CRUD lookup 选项。
 *
 * Shell 运行时在这里收口 provider 选择、依赖缺失短路、错误降级和当前值保留；
 * 元数据只声明 provider 与依赖关系，不承载加载流程或异常分支。
 */
export async function loadStandardCrudLookupOptions(
  input: LoadStandardCrudLookupOptionsInput,
): Promise<LoadStandardCrudLookupOptionsResult> {
  const { field, providers, values, detailFields } = input;

  if (field.kind !== 'lookup') {
    return { options: field.options ?? [], error: null };
  }

  const current = readCurrentOption(field, values, detailFields);
  const providerKey = field.lookup?.providerKey ?? '';
  const provider = providers.find((candidate) => candidate.key === providerKey);
  if (!provider) {
    return {
      options: preserveStandardCrudCurrentOption([], current),
      error: `未找到 lookup provider：${providerKey}`,
    };
  }

  const dependencyValues: Record<string, unknown> = {};
  for (const dependency of field.lookup?.dependsOn ?? []) {
    const dependencyValue = values[dependency];
    if (isEmptyDependencyValue(dependencyValue)) {
      return { options: [], error: null };
    }

    dependencyValues[dependency] = dependencyValue;
  }

  try {
    const options = await provider.load({
      fieldKey: field.key,
      values,
      dependencyValues,
    });

    return {
      options: preserveStandardCrudCurrentOption(options, current),
      error: null,
    };
  } catch (error) {
    return {
      options: preserveStandardCrudCurrentOption([], current),
      error: error instanceof Error && error.message.length > 0 ? error.message : '加载选项失败',
    };
  }
}
