import { isUserMessageDto, type UserMessageDto } from '@icp-platform/sdk';

export function formatTextTemplate(template: string, messageArgs: Record<string, unknown> | undefined): string {
  if (!messageArgs) {
    return template;
  }

  return template.replace(/\{([^{}]+)\}/g, (token, argName: string) => {
    if (!Object.prototype.hasOwnProperty.call(messageArgs, argName)) {
      return token;
    }

    const value = messageArgs[argName];
    if (value === null || value === undefined) return '';
    if (!['string', 'number', 'boolean'].includes(typeof value)) throw new TypeError('LOCALIZATION_ARGUMENT_MUST_BE_SCALAR');
    return String(value);
  });
}

export function formatUserMessage(
  message: UserMessageDto | null | undefined,
  t: (key: string, fallback?: string) => string,
): string {
  if (!isUserMessageDto(message)) {
    return '';
  }

  const template = t(message.messageKey, message.fallbackMessage ?? message.messageKey);
  return formatTextTemplate(template, message.messageArgs);
}
