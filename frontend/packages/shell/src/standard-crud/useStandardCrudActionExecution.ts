import { useCallback, useRef, useState } from 'react';
import { StandardCrudRequestError } from '@icp-platform/sdk';
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
  fallbackErrorMessage?: string;
}

interface UseStandardCrudActionExecutionOptions {
  refresh: () => void;
  onResult?: (result: StandardCrudOperationResult) => void;
}

/**
 * 共享动作执行 Module 统一远程动作的互斥、刷新、冲突和结果交接，
 * 让列表、详情与模块 handler 不再分别决定页面失效策略。
 */
export function useStandardCrudActionExecution<TDetail = unknown, TRow = unknown>({
  refresh,
  onResult,
}: UseStandardCrudActionExecutionOptions) {
  const pendingRef = useRef(false);
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [error, setError] = useState<StandardCrudActionExecutionError | null>(null);

  const execute = useCallback(async ({
    key,
    handler,
    context,
    refreshOnSuccess = true,
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
      if (refreshOnSuccess) {
        refresh();
      }
      if (result) {
        onResult?.(result);
        return { status: 'succeeded', result };
      }
      return { status: 'succeeded' };
    } catch (actionError) {
      const conflict = actionError instanceof StandardCrudRequestError && actionError.status === 409;
      setError({
        key,
        message: actionError instanceof Error ? actionError.message : fallbackErrorMessage,
        conflict,
      });
      if (conflict) {
        refresh();
      }
      return { status: 'failed', conflict };
    } finally {
      pendingRef.current = false;
      setPendingKey(null);
    }
  }, [onResult, refresh]);

  const clearError = useCallback(() => setError(null), []);

  return {
    pendingKey,
    error,
    execute,
    clearError,
  };
}
