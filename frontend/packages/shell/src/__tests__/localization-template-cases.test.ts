import { expect, it } from 'vitest';
import { formatTextTemplate } from '@icp-platform/core';
import cases from '../../../../../tests/TestData/localization-template-cases.json';

it.each(cases)('formats the shared template case: $template', ({ template, args, expected }) => {
  expect(formatTextTemplate(template, args)).toBe(expected);
});
it('rejects object parameters without exposing object content', () => {
  expect(() => formatTextTemplate('{value}', { value: { secret: 'never stringify' } })).toThrow('LOCALIZATION_ARGUMENT_MUST_BE_SCALAR');
});
