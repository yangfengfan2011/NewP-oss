import { describe, expect, it } from 'vitest';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { formatTextTemplate } from '../localization/userMessage';
import { formatAuthenticationError } from './messages';

describe('authentication error presentation', () => {
  it('does not reinterpret braces introduced by a scalar argument', () => {
    const catalog: Record<string, string> = { 'AuthenticationErrors.AUTH_TEST': 'Value: {value}' };
    const t = (key: string, fallback?: string, args?: Record<string, unknown>) => formatTextTemplate(catalog[key] ?? fallback ?? key, args);
    const error = new StandardCrudRequestError('diagnostic', 400, {}, {
      code: 'AUTH_TEST', args: { value: '{other}', other: 'must not replace user data' },
    });
    expect(formatAuthenticationError(error, t)).toBe('Value: {other}');
  });
});
