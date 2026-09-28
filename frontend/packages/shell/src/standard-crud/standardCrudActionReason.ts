import type { LocalizationContextValue } from '@icp-platform/core';
import type { StandardCrudActionState } from '@icp-platform/sdk';

/** 有消息事实时只解析词条；未迁移的 resolver 保留旧说明，未知词条不泄漏诊断。 */
export function formatStandardCrudActionReason(state: StandardCrudActionState | undefined, t: LocalizationContextValue['t']): string | undefined {
  if (state?.reasonKey === undefined) return state?.reason;
  return t(state.reasonKey, '', state.args)
    || t('Platform.ActionStateUnavailable', '当前状态不允许执行该操作。');
}
