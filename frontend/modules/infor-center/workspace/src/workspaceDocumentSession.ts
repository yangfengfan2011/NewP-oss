import { cancelDocumentStage, documentBusinessValues, saveDocument, stageDocumentFile, type DocumentStage } from './workspaceDocumentUpload';

export interface UploadChoice {
  readonly id: number; readonly role: 'main' | 'attachment'; readonly file: File;
  readonly state: 'queued' | 'uploading' | 'staged' | 'failed'; readonly percent?: number;
  readonly staged?: DocumentStage; readonly failure?: unknown;
}
export interface DocumentUploadSnapshot {
  readonly choices: readonly UploadChoice[];
  readonly phase: 'idle' | 'submitting' | 'committed' | 'unknown';
  readonly closed: boolean;
  readonly failure?: unknown;
  readonly tooManyAttachments?: boolean;
}

/** 一个 Drawer 的上传与保存会话；Shell 仍拥有动作执行、离开保护及成功刷新。 */
export class WorkspaceDocumentSession {
  private snapshot: DocumentUploadSnapshot = { choices: [], phase: 'idle', closed: false };
  private readonly listeners = new Set<() => void>();
  private readonly scope: Record<string, unknown> | undefined;
  private readonly scopeKey: string | undefined;
  private sequence = 0;
  private active?: { id: number; controller: AbortController };

  constructor(scope: Record<string, unknown> | undefined, private readonly featurePosition: string | undefined,
    private readonly currentValues: () => unknown) {
    this.scopeKey = JSON.stringify(scope);
    this.scope = this.scopeKey === undefined ? undefined : JSON.parse(this.scopeKey) as Record<string, unknown>;
  }

  getSnapshot(): DocumentUploadSnapshot { return this.snapshot; }
  subscribe(listener: () => void): () => void { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; }
  canSubmit(): boolean {
    return this.editable() && this.snapshot.choices.some(choice => choice.role === 'main' && choice.state === 'staged')
      && this.snapshot.choices.every(choice => choice.state === 'staged');
  }

  choose(files: readonly File[], role: UploadChoice['role']): void {
    if (!this.editable() || files.length === 0) return;
    if (role === 'attachment' && this.snapshot.choices.filter(choice => choice.role === 'attachment').length + files.length > 100) {
      this.publish({ tooManyAttachments: true });
      return;
    }
    if (role === 'main') {
      const previous = this.snapshot.choices.find(choice => choice.role === 'main');
      if (previous) this.remove(previous.id);
    }
    const selected = role === 'main' ? files.slice(0, 1) : files;
    this.publish({ failure: undefined, tooManyAttachments: false, choices: [...this.snapshot.choices,
      ...selected.map(file => ({ id: ++this.sequence, role, file, state: 'queued' as const }))] });
    void this.pump();
  }

  remove(id: number): void {
    if (!this.editable()) return;
    const choice = this.snapshot.choices.find(item => item.id === id);
    if (!choice) return;
    if (this.active?.id === id) this.active.controller.abort();
    this.publish({ choices: this.snapshot.choices.filter(item => item.id !== id), failure: undefined, tooManyAttachments: false });
    if (choice.staged) void cancelDocumentStage(choice.staged.uploadId, this.featurePosition)
      .catch(failure => { if (!this.snapshot.closed) this.publish({ failure }); });
  }

  retry(id: number): void {
    if (!this.editable() || !this.snapshot.choices.some(choice => choice.id === id && choice.state === 'failed')) return;
    this.updateChoice(id, { state: 'queued', failure: undefined });
    void this.pump();
  }

  /** 保存使用当前字段快照和会话自有 token；结果与清理资格在同一 owner 内完成裁决。 */
  async submit(values: unknown, scope: Record<string, unknown> | undefined, featurePosition: string | undefined): Promise<unknown> {
    if (this.snapshot.closed || JSON.stringify(scope) !== this.scopeKey || featurePosition !== this.featurePosition)
      throw new Error('上传会话已结束或位置已变化，请重新打开。');
    if (this.snapshot.phase !== 'idle') throw new Error('本次保存已经开始，请先核对结果。');
    if (!this.canSubmit()) throw new Error('请先完成主文件和附件上传。');
    const fields = documentBusinessValues(values);
    const mainUploadId = this.snapshot.choices.find(choice => choice.role === 'main')!.staged!.uploadId;
    const attachmentUploadIds = this.snapshot.choices.filter(choice => choice.role === 'attachment').map(choice => choice.staged!.uploadId);
    this.publish({ phase: 'submitting' });
    try {
      const payload = await saveDocument(fields, { mainUploadId, attachmentUploadIds }, this.scope, this.featurePosition);
      this.publish({ phase: 'committed' });
      return payload;
    } catch (error) {
      const code = typeof error === 'object' && error !== null && 'code' in error ? String(error.code) : '';
      this.publish({ phase: code === 'Platform.MutationOutcomeUnknown' || code === 'Persistence.CommitOutcomeUnknown' ? 'unknown' : 'idle' });
      throw error;
    }
  }

  /** 关闭只中断上传；已发业务保存继续收敛，未知或已确认使用不能被取消。 */
  close(): void {
    if (this.snapshot.closed) return;
    const abandoned = this.snapshot.choices;
    this.publish({ closed: true });
    this.active?.controller.abort();
    this.listeners.clear();
    if (this.snapshot.phase === 'idle') void (async () => {
      for (const choice of abandoned) if (choice.staged) await this.cancelAbandoned(choice.staged.uploadId);
    })();
  }

  private editable(): boolean { return !this.snapshot.closed && this.snapshot.phase === 'idle'; }
  private publish(patch: Partial<DocumentUploadSnapshot>): void {
    this.snapshot = { ...this.snapshot, ...patch };
    for (const listener of this.listeners) listener();
  }
  private updateChoice(id: number, patch: Partial<UploadChoice>): void {
    this.publish({ choices: this.snapshot.choices.map(choice => choice.id === id ? { ...choice, ...patch } : choice) });
  }
  private async cancelAbandoned(uploadId: string): Promise<void> {
    try { await cancelDocumentStage(uploadId, this.featurePosition); } catch { /* TTL 收敛无法确认的取消，不重放保存。 */ }
  }

  // 每个会话只有一个 transport；移除或结束后的迟到 token 只收敛到其原会话。
  private async pump(): Promise<void> {
    if (this.active || !this.editable()) return;
    const choice = this.snapshot.choices.find(item => item.state === 'queued');
    if (!choice) return;
    const running = { id: choice.id, controller: new AbortController() };
    this.active = running;
    this.updateChoice(choice.id, { state: 'uploading', percent: 0 });
    try {
      const result = await stageDocumentFile(choice.file, this.currentValues(), this.scope, this.featurePosition, running.controller.signal, (loaded, total) => {
        if (!this.snapshot.closed && !running.controller.signal.aborted)
          this.updateChoice(choice.id, { percent: total ? Math.min(100, Math.round(loaded / total * 100)) : undefined });
      });
      if (this.snapshot.closed || running.controller.signal.aborted || !this.snapshot.choices.some(item => item.id === choice.id)) {
        void this.cancelAbandoned(result.uploadId);
        return;
      }
      this.updateChoice(choice.id, { state: 'staged', staged: result });
    } catch (failure) {
      if (!this.snapshot.closed && !running.controller.signal.aborted) this.updateChoice(choice.id, { state: 'failed', failure });
    } finally {
      this.active = undefined;
      void this.pump();
    }
  }
}
