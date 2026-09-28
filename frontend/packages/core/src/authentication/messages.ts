import { StandardCrudRequestError } from '@icp-platform/sdk';
import { formatRequestError } from '../localization/requestError';
import type { LocalizationContextValue } from '../localization/LocalizationContext';

/** 认证拥有错误码与所属词典的绑定；结构化解析和未知错误仍使用共享边界。 */
export function formatAuthenticationError(error: unknown, t: LocalizationContextValue['t']): string {
  return formatRequestError(error instanceof TypeError
    ? new StandardCrudRequestError(error.message, 0, {}, { code: 'AUTH_NETWORK_UNAVAILABLE' }) : error,
  (key, fallback, args) => t(key, '', args) || t(`AuthenticationErrors.${key}`, fallback, args));
}
