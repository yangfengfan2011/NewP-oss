import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, it } from 'vitest';
import { formatFieldError, formatRequestError } from '@icp-platform/core';
import { readStandardCrudRequestError } from '@icp-platform/sdk';

const dictionary = JSON.parse(readFileSync(resolve(__dirname, '../../../../modules/workspace/metadata/dictionaries/Workspace.dic.json'), 'utf8')) as {
  Workspace: Record<string, { zhs: string; en: string }>;
};

const cases = [
  ['DescriptionTooLong', 400, 'Description', '说明必须是文本，且不能超过 500 个字符。', 'Description must be text and cannot exceed 500 characters.'],
  ['InvalidName', 400, 'ENAME', '文件夹名称不能为空，且不能超过 256 个字符。', 'Folder name is required and cannot exceed 256 characters.'],
  ['SiblingNameConflict', 409, 'ENAME', '当前文件夹中已有同名子文件夹，请使用其他名称。', 'A child folder with this name already exists. Choose another name.'],
  ['DuplicateReference', 409, null, '当前文件夹中已存在该对象的引用。', 'This object is already referenced by the current folder.'],
  ['CycleForbidden', 409, null, '该引用会形成文件夹循环，请选择其他文件夹。', 'This reference would create a folder cycle. Choose another folder.'],
  ['RootReferenceForbidden', 409, null, '空间根文件夹不能添加为引用。', 'A space root folder cannot be added as a reference.'],
  ['ParentWriteDenied', 403, null, '没有编辑当前文件夹内容的权限。', 'You do not have permission to edit the contents of this folder.'],
  ['FolderCreateDenied', 403, null, '没有创建文件夹的权限。', 'You do not have permission to create a folder.'],
  ['CandidateReadDenied', 403, null, '没有读取此对象类型的权限，请选择其他类型或联系管理员。', 'You do not have permission to read this object type. Choose another type or contact an administrator.'],
  ['TargetUnavailable', 404, null, '所选对象不可用或不可读取，请重新选择。', 'The selected object is unavailable or cannot be read. Select another object.'],
  ['ReferenceUnavailable', 404, null, '该引用不可用或不可读取，请刷新后重试。', 'This reference is unavailable or cannot be read. Refresh and try again.'],
  ['PositionUnavailable', 404, null, '当前位置已失效或无法访问，请刷新后重试。', 'This location is no longer valid or cannot be accessed. Refresh and try again.'],
  ['InvalidEndpoint', 400, 'target', '所选对象类型或标识无效，请重新选择。', 'The selected object type or identifier is invalid. Select another object.'],
  ['ServerFieldForbidden', 400, 'CreatedBy', '请求包含由服务端维护的字段，请刷新后重试。', 'The request contains fields maintained by the server. Refresh and try again.'],
  ['InvalidPosition', 400, null, '工作空间位置请求无效，请刷新后重试。', 'The workspace location request is invalid. Refresh and try again.'],
  ['InvalidCandidateQuery', 400, null, '候选查询参数无效，请重新打开选择窗口。', 'The candidate query parameters are invalid. Reopen the selection dialog.'],
  ['InvalidCreateInput', 400, null, '新建文件夹的请求无效，请刷新后重试。', 'The create-folder request is invalid. Refresh and try again.'],
  ['InvalidDetailQuery', 400, null, '属性查询参数无效，请重新打开属性。', 'The property query parameters are invalid. Reopen the properties.'],
  ['InvalidReferenceInput', 400, null, '引用操作的请求无效，请刷新后重试。', 'The reference request is invalid. Refresh and try again.'],
  ['InvalidPage', 400, null, '请求的页码无效，请重新查询。', 'The requested page is invalid. Run the query again.'],
  ['SubjectRequired', 403, null, '请登录后再执行工作空间操作。', 'Sign in before using workspace operations.'],
  ['UnsupportedQueryDirection', 400, null, '此工作空间查询方向不受支持。', 'This workspace query direction is not supported.'],
] as const;

it.each(cases)('presents the retained Workspace.%s API fact accurately in both languages', async (code, status, field, chinese, english) => {
  const fullCode = `Workspace.${code}`;
  const error = await readStandardCrudRequestError(new Response(JSON.stringify({ title: fullCode, status, code: fullCode, reasonKey: fullCode,
    fieldErrors: field ? [{ field, code: fullCode, reasonKey: fullCode }] : [] }), { status }));
  for (const [language, expected] of [['zhs', chinese], ['en', english]] as const) {
    const t = (key: string, fallback = '') => dictionary.Workspace[key.replace('Workspace.', '')]?.[language] ?? fallback;
    expect(formatRequestError(error, t)).toBe(expected);
    if (field) expect(formatFieldError(error.fieldErrorDetails[0], t)).toBe(expected);
  }
  expect(error.code).toBe(fullCode); expect(error.reasonKey).toBe(fullCode);
  if (field) expect(error.fieldErrorDetails[0].field).toBe(field);
});
