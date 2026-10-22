import { standardCrudRequestHeaders, StandardCrudRequestError, type StandardCrudActionInstance, type ActionDataAccessRequirement } from '@icp-platform/sdk';

export interface ObjectOwnershipTransferTarget {
  savePath: string;
  eligibilityPath: string;
  accountsPath: string;
  body?: Record<string, unknown>;
}
export interface OwnershipTransferDraft { recipientId: string; source: string }

/** 来源只固定当前动作草稿，不能代替服务端在提交事务中重验所属人及 edit。 */
const sourceOwner = Symbol('ownership-transfer-source-instance');
export function ownershipTransferSource(target: ObjectOwnershipTransferTarget, featurePosition?: string, instance?: StandardCrudActionInstance,
  dataAccess?: ActionDataAccessRequirement): string {
  const lifecycle = instance?.active ? instance.getOrCreate(sourceOwner, () => crypto.randomUUID(), () => {}) : null;
  return JSON.stringify([target.savePath, target.eligibilityPath, target.accountsPath, target.body, featurePosition, dataAccess?.id, lifecycle]);
}

/** 准备、候选与提交消费同一正式来源，声明只传引用，不进入业务载荷。 */
export function ownershipTransferHeaders(featurePosition?: string, dataAccess?: ActionDataAccessRequirement): Record<string, string> {
  return standardCrudRequestHeaders({ featurePosition, dataAccess });
}

class TransferSubmission {
  private frozen = false;
  private readonly listeners = new Set<() => void>();
  readonly getSnapshot = () => this.frozen;
  readonly subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  freeze() { if (!this.frozen) { this.frozen = true; for (const listener of this.listeners) listener(); } }
  dispose() { this.listeners.clear(); }
}
const submissionOwner = Symbol('ownership-transfer-submission');

/** 未知转交归当前账号页面实例及完整选择来源持有，关闭弹层不会恢复重复提交资格。 */
export function ownershipTransferSubmission(instance: StandardCrudActionInstance | undefined, source: string) {
  if (!instance?.active) return undefined;
  const states = instance.getOrCreate(submissionOwner, () => new Map<string, TransferSubmission>(), values => {
    for (const state of values.values()) state.dispose();
    values.clear();
  });
  let state = states.get(source);
  if (!state) { state = new TransferSubmission(); states.set(source, state); }
  return state;
}

/** 明确业务拒绝保留草稿；发送后的连接故障、服务端错误及未知回执不能自动重放。 */
export function ownershipTransferOutcomeUnknown(failure: unknown): boolean {
  return !!failure && (!(failure instanceof StandardCrudRequestError) || failure.status === 0 || failure.status >= 500
    || failure.code === 'Persistence.CommitOutcomeUnknown' || failure.code === 'Platform.MutationOutcomeUnknown');
}
