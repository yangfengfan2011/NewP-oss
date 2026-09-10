import { useCallback, useRef, useState } from 'react';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { handoffStandardCrudSuccess } from './standardCrudSuccessHandoff';
import type {
  StandardCrudActionContext,
  StandardCrudActionHandler,
  StandardCrudOperationResult,
} from '@icp-platform/sdk';

export interface StandardCrudActionExecutionError {
  key: string;
  message: string;
  conflict: boolean;
}

export type StandardCrudActionExecutionOutcome =
  | { status: 'succeeded'; result?: StandardCrudOperationResult }
  | { status: 'failed'; conflict: boolean }
  | { status: 'ignored' };

interface StandardCrudActionExecutionRequest<TDetail, TRow> {
  key: string;
  handler: StandardCrudActionHandler<TDetail, TRow>;
  context: StandardCrudActionContext<TDetail, TRow>;
  refreshOnSuccess?: boolean;
  refreshOnConflict?: boolean;
  fallbackErrorMessage?: string;
}

interface UseStandardCrudActionExecutionOptions {
  refresh: () => void;
  resourceKey?: string;
  onCommitted?: (change: import("@icp-platform/sdk").ResourceChange) => Promise<void>;
  onResult?: (result: StandardCrudOperationResult) => void;
}

/**
 * 共享动作执行 Module 统一远程动作的互斥、刷新、冲突和结果交接，
 * 让列表、详情与模块 handler 不再分别决定页面失效策略。
 */
export function useStandardCrudActionExecution<TDetail = unknown, TRow = unknown>({
  refresh,
  onResult, resourceKey, onCommitted,
}: UseStandardCrudActionExecutionOptions) {
  const [synchronizationError, setSynchronizationError] = useState<string | null>(null);
  const pendingRef = useRef(false);
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [error, setError] = useState<StandardCrudActionExecutionError | null>(null);

  const execute = useCallback(async ({
    key,
    handler,
    context,
    refreshOnSuccess = true,
    refreshOnConflict = true,
    fallbackErrorMessage = '执行操作失败',
  }: StandardCrudActionExecutionRequest<TDetail, TRow>): Promise<StandardCrudActionExecutionOutcome> => {
    if (pendingRef.current) {
      return { status: 'ignored' };
    }

    pendingRef.current = true;
    setPendingKey(key);
    setError(null);
    try {
      const result = await handler(context);
      // 业务成功已确定；呈现和组合只读失败不能把动作标成失败或重放。
      setSynchronizationError(null);
      await handoffStandardCrudSuccess({
        owner: { kind: 'action', resourceKey, refreshOnSuccess },
        result, onResult, onCommitted, refresh,
        onSynchronizationError: failure => setSynchronizationError(`已保存，刷新失败：${String(failure)}`),
      });
      return { status: 'succeeded', ...(result ? { result } : {}) };
    } catch (actionError) {
      const conflict = actionError instanceof StandardCrudRequestError && actionError.status === 409;
      setError({
        key,
        message: actionError instanceof Error ? actionError.message : fallbackErrorMessage,
        conflict,
      });
      if (conflict && refreshOnConflict) {
        refresh();
      }
      return { status: 'failed', conflict };
    } finally {
      pendingRef.current = false;
      setPendingKey(null);
    }
  }, [onCommitted, onResult, refresh, resourceKey]);

  const clearError = useCallback(() => setError(null), []);

  return {
    pendingKey,
    synchronizationError,
    error,
    execute,
    clearError,
  };
}
