import { isUserMessageDto, type UserMessageDto } from '@icp-platform/sdk';

function replaceTemplateArgs(template: string, messageArgs: Record<string, unknown> | undefined): string {
  if (!messageArgs) {
    return template;
  }

  return template.replace(/\{([^{}]+)\}/g, (token, argName: string) => {
    if (!Object.prototype.hasOwnProperty.call(messageArgs, argName)) {
      return token;
    }

    const value = messageArgs[argName];
    return value === null || value === undefined ? '' : String(value);
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
  return replaceTemplateArgs(template, message.messageArgs);
}
