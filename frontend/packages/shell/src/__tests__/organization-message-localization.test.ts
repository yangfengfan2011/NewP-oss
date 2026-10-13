import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, it } from 'vitest';
import { formatFieldError, formatRequestError } from '@icp-platform/core';
import { readStandardCrudRequestError } from '@icp-platform/sdk';

it.each([
  ['Company', 'NameDuplicated', '公司名称必须全局唯一。', 'Company names must be unique across the organization.'],
  ['Department', 'ParentCycle', '上级部门不能是当前部门的后代。', 'The parent department cannot be a descendant of this department.'],
  ['Position', 'DeleteBlockedByPersonReference', '该岗位仍被人员引用，不能删除。', 'This position is assigned to a person and cannot be deleted.'],
])('presents %s %s from retained API facts in both languages', async (resource, code, chinese, english) => {
  const document = JSON.parse(readFileSync(resolve(__dirname, `../../../../modules/platform/organization/metadata/dictionaries/Organization.${resource}.dic.json`), 'utf8')) as Record<string, Record<string, { zhs: string; en: string }>>;
  const fullCode = `Organization.${resource}.${code}`;
  const response = new Response(JSON.stringify({ code: fullCode, reasonKey: `organization.${resource.toLowerCase()}.${code[0].toLowerCase()}${code.slice(1)}`, message: 'diagnostic only', fieldErrors: [{ field: 'ENAME', code: fullCode, message: 'diagnostic only' }] }), { status: 409 });
  const error = await readStandardCrudRequestError(response);
  for (const [language, expected] of [['zhs', chinese], ['en', english]] as const) {
    const t = (key: string, fallback = '') => document[`Organization.${resource}`]?.[key.replace(`Organization.${resource}.`, '')]?.[language] ?? fallback;
    expect(formatRequestError(error, t)).toBe(expected);
    expect(formatFieldError(error.fieldErrorDetails[0], t)).toBe(expected);
  }
  expect(error.code).toBe(fullCode);
  expect(error.message).toBe('diagnostic only');
});
