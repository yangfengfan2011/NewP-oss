import { isStandardCrudRecord, StandardCrudRequestError, type StandardCrudFieldErrorFact } from '@icp-platform/sdk';
import type { LocalizationContextValue } from './LocalizationContext';

type Translate = LocalizationContextValue['t'];

/** 原始诊断留在 Error；用户提示只解析已登记的完整词条，未知原因显示公共提示与已有公开标识。 */
export function formatRequestError(error: unknown, t: Translate): string {
  const generic = t('Platform.UnexpectedError', '操作失败，请稍后重试。');
  if (!(error instanceof StandardCrudRequestError)) return generic;
  const specific = (error.reasonKey ? t(error.reasonKey, '', error.args) : '')
    || (error.code ? t(error.code, '', error.args) : '');
  if (specific) return specific;
  const fallback = error.status === 401 ? t('Platform.Unauthorized', generic)
    : error.status === 403 ? t('Platform.Forbidden', generic)
    : error.status === 409 ? t('Platform.MutationConflict', generic)
    : error.fieldErrorDetails.length > 0 ? t('Platform.ValidationFailed', generic) : generic;
  // 只展示公开结构化标识；缺少标识时保留原提示，不从诊断推断或生成 ID。
  const traceId = isStandardCrudRecord(error.responseProblem) ? error.responseProblem.traceId : undefined;
  const identifiers = [
    typeof error.code === 'string' && error.code.trim() ? `code=${error.code}` : '',
    typeof traceId === 'string' && traceId.trim() ? `traceId=${traceId}` : '',
  ].filter(Boolean);
  return identifiers.length > 0 ? `${fallback} ${t('Platform.Diagnostics', '诊断信息')}: ${identifiers.join('; ')}` : fallback;
}

/** 字段标识不经翻译，只有消息事实在当前语言下呈现。 */
export function formatFieldError(error: StandardCrudFieldErrorFact, t: Translate): string {
  return (error.reasonKey ? t(error.reasonKey, '', error.args) : '')
    || (error.code ? t(error.code, '', error.args) : '')
    || t('Platform.ValidationFailed', '请检查输入的字段。');
}
