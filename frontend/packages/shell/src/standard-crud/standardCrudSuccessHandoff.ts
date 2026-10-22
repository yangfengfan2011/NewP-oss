import type { ResourceChange, StandardCrudOperationResult } from '@icp-platform/sdk';

type StandardCrudSuccessOwner =
  | { kind: 'action'; resourceKey?: string; refreshOnSuccess?: boolean }
  | { kind: 'mutation'; resourceKey: string };

interface StandardCrudSuccessHandoff {
  owner: StandardCrudSuccessOwner;
  result: StandardCrudOperationResult | void;
  onResult?: (result: StandardCrudOperationResult) => void;
  onCommitted?: (change: ResourceChange) => Promise<void>;
  refresh: () => void;
  onSynchronizationError: (error: unknown) => void;
}

// 成功事实只交接一次；结果登记和本地刷新仅同步触发，不等待呈现或网络完成。
export async function handoffStandardCrudSuccess({
  owner, result, onResult, onCommitted, refresh, onSynchronizationError,
}: StandardCrudSuccessHandoff): Promise<void> {
  const unchanged = result?.noChange === true && result.operation === 'action' && !result.change;
  // 非法回执仍按实际 change 更新消费者；不能因为无变更标记丢失已经成功的写入。
  if (result?.noChange && !unchanged) onSynchronizationError(new Error('Invalid no-change action receipt.'));
  const followUps = [
    () => { if (result) onResult?.(result); },
    async () => {
      if (!unchanged && result?.change && (owner.kind === 'mutation' || owner.resourceKey)) {
        await onCommitted?.({ ...result.change, resourceKey: owner.resourceKey! });
      }
    },
    () => {
      if (unchanged) return;
      const hasHandoff = result?.change && onCommitted
        && (owner.kind === 'mutation' || owner.resourceKey);
      if (!hasHandoff && (owner.kind === 'mutation' || owner.refreshOnSuccess !== false)) refresh();
    },
  ];
  for (const followUp of followUps) {
    // 各步独立隔离：交接失败不回退刷新，也不能重放已成功的写入。
    try { await followUp(); }
    catch (error) { onSynchronizationError(error); }
  }
}
