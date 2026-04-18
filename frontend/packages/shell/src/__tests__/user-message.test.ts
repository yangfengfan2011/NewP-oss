import { describe, expect, it, vi } from 'vitest';
import { formatUserMessage } from '@icp-platform/core';
import { isUserMessageDto, type UserMessageDto } from '@icp-platform/sdk';

describe('user message contract helpers', () => {
  it('recognizes the shared user message dto shape', () => {
    expect(
      isUserMessageDto({
        success: false,
        level: 'error',
        messageKey: 'Errors.InvalidState',
      }),
    ).toBe(true);

    expect(
      isUserMessageDto({
        success: false,
        level: 'critical',
        messageKey: 'Errors.InvalidState',
      }),
    ).toBe(false);
  });

  it('formats a user message by translating the catalog template and replacing placeholders', () => {
    const t = vi.fn((key: string, fallback?: string) => fallback ?? key);
    const message: UserMessageDto = {
      success: false,
      level: 'error',
      messageKey: 'Errors.DuplicateCode',
      messageArgs: {
        code: 'A-001',
      },
      fallbackMessage: '代码 {code} 已存在',
    };

    const text = formatUserMessage(message, t);

    expect(t).toHaveBeenCalledWith('Errors.DuplicateCode', '代码 {code} 已存在');
    expect(text).toBe('代码 A-001 已存在');
  });

  it('falls back to fallbackMessage when the catalog has no key', () => {
    const t = vi.fn((key: string, fallback?: string) => fallback ?? key);
    const message: UserMessageDto = {
      success: false,
      level: 'warning',
      messageKey: 'Warnings.MissingLimit',
      messageArgs: {
        limit: 3,
      },
      fallbackMessage: '最多允许 {limit} 条',
    };

    const text = formatUserMessage(message, t);

    expect(t).toHaveBeenCalledWith('Warnings.MissingLimit', '最多允许 {limit} 条');
    expect(text).toBe('最多允许 3 条');
  });

  it('does not pass through raw string input outside the shared dto contract', () => {
    const t = vi.fn((key: string, fallback?: string) => fallback ?? key);

    const text = formatUserMessage('legacy raw text', t);

    expect(text).toBe('');
    expect(t).not.toHaveBeenCalled();
  });

  it('rejects complex messageArgs values in the shared dto guard', () => {
    expect(
      isUserMessageDto({
        success: false,
        level: 'error',
        messageKey: 'Errors.InvalidState',
        messageArgs: {
          nested: { a: 1 } as unknown as string,
        },
      }),
    ).toBe(false);

    expect(
      isUserMessageDto({
        success: false,
        level: 'warning',
        messageKey: 'Warnings.InvalidState',
        messageArgs: {
          list: [1, 2, 3] as unknown as string,
        },
      }),
    ).toBe(false);
  });

  it('accepts scalar messageArgs values in the shared dto guard', () => {
    expect(
      isUserMessageDto({
        success: true,
        level: 'success',
        messageKey: 'Messages.Ready',
        messageArgs: {
          code: 'A-001',
          count: 3,
          enabled: true,
          note: null,
        },
      }),
    ).toBe(true);
  });

  it('rejects non-string optional fields in the shared dto guard', () => {
    expect(
      isUserMessageDto({
        success: true,
        level: 'success',
        messageKey: 'Messages.Ready',
        fallbackMessage: 123 as unknown as string,
      }),
    ).toBe(false);

    expect(
      isUserMessageDto({
        success: true,
        level: 'success',
        messageKey: 'Messages.Ready',
        code: { value: 'code' } as unknown as string,
      }),
    ).toBe(false);

    expect(
      isUserMessageDto({
        success: true,
        level: 'success',
        messageKey: 'Messages.Ready',
        fieldKey: ['Fields.Name'] as unknown as string,
      }),
    ).toBe(false);

    expect(
      isUserMessageDto({
        success: true,
        level: 'success',
        messageKey: 'Messages.Ready',
        actionKey: false as unknown as string,
      }),
    ).toBe(false);

    expect(
      isUserMessageDto({
        success: true,
        level: 'success',
        messageKey: 'Messages.Ready',
        statusKey: null as unknown as string,
      }),
    ).toBe(false);
  });
});
