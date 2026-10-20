import type { ActionDataAccessRequirement } from '@icp-platform/sdk';

export interface ObjectOwnershipInitializationTarget {
  initialId?: string;
  pendingPath: string;
  statusPath: string;
  savePath: string;
  accountsPath: string;
  eligibilityPath: string;
  batchPath: string;
  batchIdField?: 'ids' | 'linkIds';
  body?: Record<string, unknown>;
}
export interface OwnershipInitializationDraft { targetId?: string; targetIds?: string[]; accountId?: string; source: string }

/** 草稿只属于当前目标、位置与正式声明，不能跨页面来源提交已选择的对象。 */
export function ownershipInitializationSource(target: ObjectOwnershipInitializationTarget, featurePosition?: string,
  dataAccess?: ActionDataAccessRequirement): string {
  return JSON.stringify([target.initialId || null, target.pendingPath, target.statusPath, target.savePath, target.accountsPath,
    target.batchPath, target.batchIdField, target.body, featurePosition, dataAccess?.id]);
}

/** 资格保留原功能守卫；数据读取和提交继续透传当前动作的独立声明。 */
export function ownershipInitializationHeaders(featurePosition?: string, dataAccess?: ActionDataAccessRequirement): Record<string, string> {
  return { ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}),
    ...(dataAccess?.id ? { 'X-NewP-Data-Declaration': dataAccess.id } : {}) };
}
