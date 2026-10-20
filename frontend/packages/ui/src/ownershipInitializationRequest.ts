import { standardCrudRequestHeaders, type ActionDataAccessRequirement } from '@icp-platform/sdk';

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
  dataAccess?: ActionDataAccessRequirement, inputReadSourceKey?: string): string {
  return JSON.stringify([target.initialId || null, target.pendingPath, target.statusPath, target.savePath, target.accountsPath,
    target.batchPath, target.batchIdField, target.body, featurePosition, dataAccess?.id, inputReadSourceKey]);
}

/** 资格保留原功能守卫；查询来源和目标按钮各自使用SDK正式身份，不混传请求头。 */
export function ownershipInitializationHeaders(featurePosition?: string, dataAccess?: ActionDataAccessRequirement): Record<string, string> {
  return standardCrudRequestHeaders({ featurePosition, dataAccess });
}
