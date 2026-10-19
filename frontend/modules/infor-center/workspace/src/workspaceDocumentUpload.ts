import { platformFetch, platformUpload } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError } from '@icp-platform/sdk';
import { workspacePosition } from './workspacePosition';
import type { WorkspaceDocumentSession } from './workspaceDocumentSession';

export interface WorkspaceDocumentDraft extends Record<string, unknown> {
  session?: WorkspaceDocumentSession;
}
export interface DocumentStage { uploadId: string; fileId: string; fileName: string; length: number; contentType: string; expiresAt: string }

/** 每个写入沿当前完整位置；业务字段不夹带上传、生命周期或任意服务端身份。 */
export function documentBusinessValues(input: unknown): Record<string, unknown> {
  const record = typeof input === 'object' && input !== null ? input as Record<string, unknown> : {};
  return Object.fromEntries(['ECODE', 'ENAME', 'Description'].filter(key => key in record).map(key => [key, record[key]]));
}

/** 显式用户选择触发单上传，正式 transport 提供真实进度与取消。 */
export async function stageDocumentFile(file: File, draft: unknown, scope: Record<string, unknown> | undefined, featurePosition: string | undefined,
  signal: AbortSignal, onProgress: (loaded: number, total?: number) => void, declarationId?: string): Promise<DocumentStage> {
  if (!declarationId) throw new StandardCrudRequestError('缺少当前图文档上传声明。', 400);
  const body = new FormData();
  body.append('position', JSON.stringify(workspacePosition(scope))); body.append('values', JSON.stringify(documentBusinessValues(draft)));
  body.append('file', file);
  const response = await platformUpload('/api/workspace/content/document-uploads', body,
    { signal, onProgress, headers: { ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}), 'X-NewP-Data-Declaration': declarationId } });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return await response.json() as DocumentStage;
}

/** 只取消未提交暂存；已知/未知业务提交的输入所有者不会调用此操作。 */
export async function cancelDocumentStage(uploadId: string, featurePosition?: string): Promise<void> {
  const response = await platformFetch(`/api/workspace/content/document-uploads/${encodeURIComponent(uploadId)}`,
    { method: 'DELETE', headers: featurePosition ? { 'X-NewP-Feature': featurePosition } : {} });
  if (!response.ok) throw await readStandardCrudRequestError(response);
}

/** 显式保存只投影业务字段与上传身份；成功回执读不完整仍保留未知提交语义。 */
export async function saveDocument(values: unknown, uploads: { mainUploadId: string; attachmentUploadIds: string[] },
  scope: Record<string, unknown> | undefined, featurePosition?: string, declarationId?: string): Promise<unknown> {
  if (!declarationId) throw new StandardCrudRequestError('缺少当前图文档创建声明。', 400);
  const response = await platformFetch('/api/workspace/content/documents', { method: 'POST',
    headers: { ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}), 'X-NewP-Data-Declaration': declarationId, 'Content-Type': 'application/json' },
    body: JSON.stringify({ position: workspacePosition(scope), values: documentBusinessValues(values),
      uploads: { mainUploadId: uploads.mainUploadId, ...(uploads.attachmentUploadIds.length ? { attachmentUploadIds: uploads.attachmentUploadIds } : {}) } }) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  try { return await response.json() as unknown; }
  catch (cause) {
    throw new StandardCrudRequestError('操作结果尚未确认，请先查询实际结果，再决定是否重试。', response.status, {}, {
      code: 'Platform.MutationOutcomeUnknown', reasonKey: 'Platform.MutationOutcomeUnknown',
    }, undefined, cause);
  }
}
