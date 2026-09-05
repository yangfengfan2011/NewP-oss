import { useCallback, useRef, useState } from 'react';
import {
  StandardCrudRequestError,
  normalizeStandardCrudSubmitValues,
  type StandardCrudDataAdapter,
  type StandardCrudFieldDefinition,
  type StandardCrudOperationResult,
} from '@icp-platform/sdk';

const STANDARD_CRUD_MUTATION_CONFLICT_MESSAGE = '记录已发生变化，未覆盖服务器上的最新数据。请重新加载后再编辑。';

export interface StandardCrudMutationExecutionRequest {
  mode: 'create' | 'edit';
  id?: string;
  fields: StandardCrudFieldDefinition[];
  values: Record<string, unknown>;
  detailFields: Record<string, unknown>;
}

export interface StandardCrudMutationExecution {
  pending: boolean;
  error: string | null;
  fieldErrors: Record<string, string>;
  conflict: boolean;
  execute: (request: StandardCrudMutationExecutionRequest) => Promise<boolean>;
  clearError: () => void;
}

export interface StandardCrudMutationRegistration {
  resourceKey: string;
  dataAdapter: Pick<StandardCrudDataAdapter, 'create' | 'update'>;
  buildMutationValues?: (values: Record<string, unknown>) => Record<string, unknown>;
  concurrencyRevisionField?: string;
}

interface UseStandardCrudMutationExecutionOptions {
  registration: StandardCrudMutationRegistration;
  refresh: () => void;
  onResult?: (result: StandardCrudOperationResult) => void;
}

function mapMutationFieldErrors(
  fields: StandardCrudFieldDefinition[],
  fieldErrors: Record<string, string>,
): Record<string, string> {
  const fieldByModel = new Map(fields.map((field) => [field.modelField, field.key]));
  return Object.fromEntries(
    Object.entries(fieldErrors).map(([key, value]) => [fieldByModel.get(key) ?? key, value]),
  );
}

/**
 * 共享普通 create/update 提交流程，统一互斥、错误映射、冲突归类与成功刷新/结果交接。
 */
export function useStandardCrudMutationExecution({
  registration,
  refresh,
  onResult,
}: UseStandardCrudMutationExecutionOptions): StandardCrudMutationExecution {
  const pendingRef = useRef(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [conflict, setConflict] = useState(false);

  const clearError = useCallback(() => {
    setError(null);
    setFieldErrors({});
    setConflict(false);
  }, []);

  const execute = useCallback(async ({
    mode,
    id,
    fields,
    values,
    detailFields,
  }: StandardCrudMutationExecutionRequest): Promise<boolean> => {
    if (pendingRef.current) {
      return false;
    }

    pendingRef.current = true;
    setPending(true);
    clearError();

    try {
      const builtValues = registration.buildMutationValues
        ? registration.buildMutationValues(values)
        : normalizeStandardCrudSubmitValues(fields, values);
      const normalizedValues = { ...builtValues };
      for (const field of fields) {
        if (field.readOnly) {
          delete normalizedValues[field.modelField];
        }
      }
      const mutationValues = mode === 'edit'
        && registration.concurrencyRevisionField
        && Object.prototype.hasOwnProperty.call(detailFields, registration.concurrencyRevisionField)
        && detailFields[registration.concurrencyRevisionField] !== undefined
        ? {
            ...normalizedValues,
            [registration.concurrencyRevisionField]: detailFields[registration.concurrencyRevisionField],
          }
        : normalizedValues;

      let result: StandardCrudOperationResult | undefined;
      if (mode === 'create') {
        if (!registration.dataAdapter.create) {
          throw new Error(`标准资源 "${registration.resourceKey}" 未提供创建能力。`);
        }

        result = await registration.dataAdapter.create(mutationValues, detailFields);
      } else {
        if (!id) {
          throw new Error('缺少资源标识，无法保存修改。');
        }
        if (!registration.dataAdapter.update) {
          throw new Error(`标准资源 "${registration.resourceKey}" 未提供更新能力。`);
        }

        result = await registration.dataAdapter.update(id, mutationValues, detailFields);
      }

      refresh();
      if (result) {
        onResult?.(result);
      }

      return true;
    } catch (mutationError) {
      if (mutationError instanceof StandardCrudRequestError) {
        setFieldErrors(mapMutationFieldErrors(fields, mutationError.fieldErrors));
        setConflict(mutationError.status === 409);
        setError(mutationError.status === 409
          ? STANDARD_CRUD_MUTATION_CONFLICT_MESSAGE
          : mutationError.message);
      } else {
        setError(mutationError instanceof Error ? mutationError.message : '保存标准资源失败');
      }

      return false;
    } finally {
      pendingRef.current = false;
      setPending(false);
    }
  }, [clearError, onResult, refresh, registration]);

  return {
    pending,
    error,
    fieldErrors,
    conflict,
    execute,
    clearError,
  };
}
