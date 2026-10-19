import { useCallback, useRef, useState } from 'react';
import { formatFieldError, formatRequestError, useLocalization } from '@icp-platform/core';
import {
  StandardCrudRequestError,
  normalizeStandardCrudSubmitValues,
  type StandardCrudDataAdapter,
  type StandardCrudFieldDefinition,
  type StandardCrudOperationResult,
  type StandardCrudFieldErrorFact,
  type ActionDataAccessRequirement,
} from '@icp-platform/sdk';
import { handoffStandardCrudSuccess } from './standardCrudSuccessHandoff';

const STANDARD_CRUD_MUTATION_CONFLICT_MESSAGE = '记录已发生变化，未覆盖服务器上的最新数据。请重新加载后再编辑。';

export interface StandardCrudMutationExecutionRequest {
  mode: 'create' | 'edit';
  featurePosition?: string;
  dataAccess?: ActionDataAccessRequirement;
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
  synchronizationError: string | null;
  synchronizationFailure: unknown;
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
  onCommitted?: (change: import("@icp-platform/sdk").ResourceChange) => Promise<void>;
  fixedCreateValues?: Record<string, unknown>;
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
  onCommitted,
  fixedCreateValues,
}: UseStandardCrudMutationExecutionOptions): StandardCrudMutationExecution {
  const { t } = useLocalization();
  const pendingRef = useRef(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [requestError, setRequestError] = useState<unknown>(null);
  const [fieldErrorFacts, setFieldErrorFacts] = useState<Record<string, StandardCrudFieldErrorFact>>({});
  const [conflict, setConflict] = useState(false);
  const [synchronizationFailure, setSynchronizationFailure] = useState<{ error: unknown } | null>(null);

  const clearError = useCallback(() => {
    setError(null);
    setRequestError(null);
    setFieldErrorFacts({});
    setFieldErrors({});
    setConflict(false);
  }, []);

  const execute = useCallback(async ({
    mode,
    featurePosition,
    dataAccess,
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

      const context = featurePosition || dataAccess
        ? { ...(featurePosition ? { featurePosition } : {}), ...(dataAccess ? { dataAccess } : {}) }
        : undefined;
      let result: StandardCrudOperationResult | undefined;
      if (mode === 'create') {
        if (!registration.dataAdapter.create) {
          throw new Error(`标准资源 "${registration.resourceKey}" 未提供创建能力。`);
        }

        result = await registration.dataAdapter.create({ ...mutationValues, ...fixedCreateValues }, detailFields, ...(context ? [context] : []));
      } else {
        if (!id) {
          throw new Error('缺少资源标识，无法保存修改。');
        }
        if (!registration.dataAdapter.update) {
          throw new Error(`标准资源 "${registration.resourceKey}" 未提供更新能力。`);
        }

        result = await registration.dataAdapter.update(id, mutationValues, detailFields, ...(context ? [context] : []));
      }

      // 写入已经提交；后置呈现或读取失败不能进入写入错误分支，也不能重放写入。
      setSynchronizationFailure(null);
      await handoffStandardCrudSuccess({
        owner: { kind: 'mutation', resourceKey: registration.resourceKey },
        result, onResult, onCommitted, refresh,
        onSynchronizationError: readError => setSynchronizationFailure({ error: readError }),
      });

      return true;
    } catch (mutationError) {
      setRequestError(mutationError);
      if (mutationError instanceof StandardCrudRequestError) {
        setFieldErrors(mapMutationFieldErrors(fields, mutationError.fieldErrors));
        const fieldByModel = new Map(fields.map(field => [field.modelField, field.key]));
        setFieldErrorFacts(Object.fromEntries(mutationError.fieldErrorDetails.map(fact => [fieldByModel.get(fact.field) ?? fact.field, fact])));
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
  }, [clearError, fixedCreateValues, onCommitted, onResult, refresh, registration]);

  return {
    pending,
    error: error ? formatRequestError(requestError, t) : null,
    fieldErrors: Object.fromEntries(Object.keys(fieldErrors).map(field => [field, formatFieldError(fieldErrorFacts[field] ?? { field }, t)])),
    conflict,
    synchronizationFailure: synchronizationFailure?.error,
    synchronizationError: synchronizationFailure ? t('Platform.SavedRefreshFailed', '已保存，刷新失败。请重新加载。') : null,
    execute,
    clearError,
  };
}
