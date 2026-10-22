export const USER_MESSAGE_LEVELS = ['info', 'success', 'warning', 'error'] as const;

export type UserMessageLevel = (typeof USER_MESSAGE_LEVELS)[number];

export type UserMessageArgValue = string | number | boolean | null;
export type UserMessageArgs = Record<string, UserMessageArgValue>;

export interface UserMessageDto {
  success: boolean;
  level: UserMessageLevel;
  messageKey: string;
  messageArgs?: UserMessageArgs;
  fallbackMessage?: string;
  code?: string;
  fieldKey?: string;
  actionKey?: string;
  statusKey?: string;
}

export function isUserMessageDto(value: unknown): value is UserMessageDto {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Partial<UserMessageDto>;
  if (typeof candidate.success !== 'boolean') {
    return false;
  }

  if (typeof candidate.messageKey !== 'string') {
    return false;
  }

  if (typeof candidate.level !== 'string' || !USER_MESSAGE_LEVELS.includes(candidate.level as UserMessageLevel)) {
    return false;
  }

  if (candidate.fallbackMessage !== undefined && typeof candidate.fallbackMessage !== 'string') {
    return false;
  }

  if (candidate.code !== undefined && typeof candidate.code !== 'string') {
    return false;
  }

  if (candidate.fieldKey !== undefined && typeof candidate.fieldKey !== 'string') {
    return false;
  }

  if (candidate.actionKey !== undefined && typeof candidate.actionKey !== 'string') {
    return false;
  }

  if (candidate.statusKey !== undefined && typeof candidate.statusKey !== 'string') {
    return false;
  }

  if (candidate.messageArgs !== undefined) {
    const messageArgs = candidate.messageArgs;
    if (!messageArgs || typeof messageArgs !== 'object' || Array.isArray(messageArgs)) {
      return false;
    }

    for (const value of Object.values(messageArgs as Record<string, unknown>)) {
      if (!isAllowedUserMessageArgValue(value)) {
        return false;
      }
    }
  }

  return true;
}

function isAllowedUserMessageArgValue(value: unknown): value is UserMessageArgValue {
  if (value === null) {
    return true;
  }

  if (typeof value === 'string' || typeof value === 'boolean') {
    return true;
  }

  return typeof value === 'number' && Number.isFinite(value);
}
