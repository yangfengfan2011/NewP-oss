import { StandardCrudRequestError, type StandardCrudFieldErrorFact } from '@icp-platform/sdk';
import type { LocalizationContextValue } from './LocalizationContext';

type Translate = LocalizationContextValue['t'];

/** 原始诊断留在 Error；用户提示只解析已登记的完整词条，未知原因显示公共提示。 */
export function formatRequestError(error: unknown, t: Translate): string {
  const generic = t('Platform.UnexpectedError', '操作失败，请稍后重试。');
  if (!(error instanceof StandardCrudRequestError)) return generic;
  if (error.status === 401) return t('Platform.Unauthorized', generic);
  if (error.status === 403) return t('Platform.Forbidden', generic);
  if (error.status === 409) return t('Platform.MutationConflict', generic);
  return (error.reasonKey ? t(error.reasonKey, '', error.args) : '')
    || (error.code ? t(error.code, '', error.args) : '')
    || (error.fieldErrorDetails.length > 0 ? t('Platform.ValidationFailed', generic) : generic);
}

/** 字段标识不经翻译，只有消息事实在当前语言下呈现。 */
export function formatFieldError(error: StandardCrudFieldErrorFact, t: Translate): string {
  return (error.reasonKey ? t(error.reasonKey, '', error.args) : '')
    || (error.code ? t(error.code, '', error.args) : '')
    || t('Platform.ValidationFailed', '请检查输入的字段。');
}
