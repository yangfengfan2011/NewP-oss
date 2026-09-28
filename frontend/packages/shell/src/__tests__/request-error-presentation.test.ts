import { expect, it } from 'vitest';
import { formatFieldError, formatRequestError, formatTextTemplate } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError } from '@icp-platform/sdk';
import sharedErrors from '../../../../../modules/Platform/metadata/dictionaries/StandardCrud.dic.json';

const catalog: Record<string, string> = {
  'Platform.UnexpectedError': 'Please try again.',
  'Platform.Unauthorized': 'Your session has expired.',
  'Platform.Forbidden': 'Permission denied.',
  'Platform.MutationConflict': 'The record has changed.',
  'Platform.ValidationFailed': 'Check the fields.',
  'Authentication.AdmissionDenied': 'Sign-in denied: {reason}.',
  AUTH_ADMISSION_DENIED: 'Account cannot sign in: {reason}.',
  CREDENTIAL_CONFLICT: 'Credential conflict: {reason}.',
};
const t = (key: string, fallback?: string, args?: Record<string, unknown>) =>
  formatTextTemplate(catalog[key] ?? fallback ?? key, args);

it('prefers a registered reasonKey to both the code and 401 fallback while preserving arguments', () => {
  const error = new StandardCrudRequestError('raw diagnostic', 401, {}, {
    code: 'AUTH_ADMISSION_DENIED', reasonKey: 'Authentication.AdmissionDenied', args: { reason: 'inactive personnel' },
  });
  expect(formatRequestError(error, t)).toBe('Sign-in denied: inactive personnel.');
  expect(error.args).toEqual({ reason: 'inactive personnel' });
});

it('uses a registered code when the reasonKey is unknown', () => {
  const error = new StandardCrudRequestError('raw diagnostic', 401, {}, {
    code: 'AUTH_ADMISSION_DENIED', reasonKey: 'Unregistered.Reason', args: { reason: 'inactive department' },
  });
  expect(formatRequestError(error, t)).toBe('Account cannot sign in: inactive department.');
});

it.each([401, 403, 409])('preserves a specific registered reason at status %s', status => {
  const error = new StandardCrudRequestError('raw diagnostic', status, {}, {
    code: 'CREDENTIAL_CONFLICT', args: { reason: 'already changed' },
  });
  expect(formatRequestError(error, t)).toBe('Credential conflict: already changed.');
});

it.each([
  [401, 'Your session has expired.'],
  [403, 'Permission denied.'],
  [409, 'The record has changed.'],
  [500, 'Please try again.'],
])('uses the current-language fallback for unknown facts at status %s', (status, expected) => {
  const error = new StandardCrudRequestError('raw diagnostic', Number(status), {}, {
    code: 'Unregistered.Code', reasonKey: 'Unregistered.Reason', args: { reason: 'raw detail' },
  });
  expect(formatRequestError(error, t)).toBe(expected);
});

it('keeps unknown field errors on the field-validation fallback', () => {
  const error = new StandardCrudRequestError('raw diagnostic', 400, {}, {
    fieldErrors: [{ field: 'NewPassword', code: 'Unregistered.Code' }],
  });
  expect(formatRequestError(error, t)).toBe('Check the fields.');
});

it('never displays an unstructured raw diagnostic', () => {
  expect(formatRequestError(new Error('raw diagnostic'), t)).toBe('Please try again.');
});

it.each([
  ['zhs', '文本不能超过 256 个字符。'],
  ['en', 'Text cannot exceed 256 characters.'],
] as const)('presents real public length-error authoring in %s without losing response facts', async (locale, expected) => {
  const problem = {
    statusCode: 400,
    code: 'StandardCrud.ShortTextTooLong',
    reasonKey: 'standardCrud.shortTextTooLong',
    message: '字段 ENAME 不能超过 256 个字符。',
    args: { maxLength: 256 },
    fieldErrors: [{
      field: 'ENAME', code: 'StandardCrud.ShortTextTooLong', reasonKey: 'standardCrud.shortTextTooLong',
      args: { maxLength: 256 }, message: '字段 ENAME 不能超过 256 个字符。',
    }],
  };
  const error = await readStandardCrudRequestError(new Response(JSON.stringify(problem), { status: 400 }));
  const translate = (key: string, fallback?: string, args?: Record<string, unknown>) =>
    formatTextTemplate(key === problem.code ? sharedErrors.StandardCrud.ShortTextTooLong[locale] : fallback ?? '', args);
  expect(formatRequestError(error, translate)).toBe(expected);
  expect(formatFieldError(error.fieldErrorDetails[0], translate)).toBe(expected);
  expect(error.responseProblem).toEqual(problem);
  expect(error.args).toEqual({ maxLength: 256 });
  expect(error.fieldErrorDetails[0].args).toEqual({ maxLength: 256 });
  expect(error.fieldErrorDetails[0].field).toBe('ENAME');
  expect(error.message).toBe(problem.message);
});

it.each([
  ['zhs', '请去除首尾空白，空值请提交 null。'],
  ['en', 'Trim leading and trailing whitespace; submit null for an empty value.'],
] as const)('presents the public noncanonical-input error in %s', (locale, expected) => {
  const code = 'StandardCrud.ShortTextNotNormalized';
  const error = new StandardCrudRequestError('raw diagnostic', 400, {}, {
    code, reasonKey: 'standardCrud.shortTextNotNormalized',
  });
  const translate = (key: string, fallback?: string) =>
    key === code ? sharedErrors.StandardCrud.ShortTextNotNormalized[locale] : fallback ?? '';
  expect(formatRequestError(error, translate)).toBe(expected);
});
