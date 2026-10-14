import { platformFetch, platformUpload } from '@icp-platform/core';
import { readStandardCrudRequestError } from '@icp-platform/sdk';
import { workspacePosition } from './workspacePosition';

export interface DocumentSubmissionLifecycle { begin(): boolean; committed(): void; failed(unknown: boolean): void }
export interface WorkspaceDocumentDraft extends Record<string, unknown> {
  mainUploadId?: string;
  attachmentUploadIds?: string[];
  submission?: DocumentSubmissionLifecycle;
}
export interface DocumentStage { uploadId: string; fileId: string; fileName: string; length: number; contentType: string; expiresAt: string }

/** 每个写入沿当前完整位置；业务字段不夹带上传、生命周期或任意服务端身份。 */
export function documentBusinessValues(input: unknown): Record<string, unknown> {
  const record = typeof input === 'object' && input !== null ? input as Record<string, unknown> : {};
  return Object.fromEntries(['ECODE', 'ENAME', 'Description'].filter(key => key in record).map(key => [key, record[key]]));
}

/** 显式用户选择触发单上传，正式 transport 提供真实进度与取消。 */
export async function stageDocumentFile(file: File, draft: unknown, scope: Record<string, unknown> | undefined, featurePosition: string | undefined,
  signal: AbortSignal, onProgress: (loaded: number, total?: number) => void): Promise<DocumentStage> {
  const body = new FormData();
  body.append('position', JSON.stringify(workspacePosition(scope))); body.append('values', JSON.stringify(documentBusinessValues(draft)));
  body.append('file', file);
  const response = await platformUpload('/api/workspace/content/document-uploads', body,
    { signal, onProgress, headers: featurePosition ? { 'X-NewP-Feature': featurePosition } : {} });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return await response.json() as DocumentStage;
}

/** 只取消未提交暂存；已知/未知业务提交的输入所有者不会调用此操作。 */
export async function cancelDocumentStage(uploadId: string, featurePosition?: string): Promise<void> {
  const response = await platformFetch(`/api/workspace/content/document-uploads/${encodeURIComponent(uploadId)}`,
    { method: 'DELETE', headers: featurePosition ? { 'X-NewP-Feature': featurePosition } : {} });
  if (!response.ok) throw await readStandardCrudRequestError(response);
}
