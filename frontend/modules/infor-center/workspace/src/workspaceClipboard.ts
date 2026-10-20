import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionInstance } from '@icp-platform/sdk';

export interface ClipboardPosition { spaceId: string; rootFolderId: string; linkIds: readonly string[] }
export interface WorkspaceClipboard { readonly mode: 'copy' | 'cut'; readonly sourcePosition: ClipboardPosition; readonly linkIds: readonly string[] }
export interface ClipboardPasteReceipt { readonly succeeded: true; readonly noChange?: boolean }
export interface ClipboardState { readonly phase: 'idle' | 'validating' | 'pasting' | 'unknown'; readonly clipboard?: WorkspaceClipboard }
export type FolderCopyKind = 'copy' | 'copy-structure';
export interface FolderCopyDraft { readonly kind: FolderCopyKind; readonly declarationId: string; readonly source: WorkspaceClipboard; readonly targetKey: string; readonly featurePosition?: string; closed: boolean }
export interface FolderCopyPreparation { readonly sourceName: string; readonly sourcePath: readonly string[]; readonly targetPath: readonly string[] }
export interface ObjectCopyDraft { readonly source: WorkspaceClipboard; readonly model: 'Part' | 'Document'; readonly declarationId: string; readonly targetKey: string; readonly featurePosition?: string; operationId?: string; requiresReconfirmation?: boolean; fieldErrors?: Record<string, string>; closed: boolean }
export interface ObjectCopyValuesRow { sourceLinkId: string; values: Record<string, unknown> }
export interface ObjectCopyPreparationItem extends ObjectCopyValuesRow { sourcePath: readonly string[]; fileCount: number; totalBytes: number }
export interface ObjectCopyPreparation { operationId: string; values: Record<string, unknown>; sourcePath: readonly string[]; targetPath: readonly string[]; modelKey?: 'Part' | 'Document'; fileCount?: number; totalBytes?: number; items?: ObjectCopyPreparationItem[] }
export interface ObjectCopyReceipt { succeeded: boolean; state: 'ready' | 'preparing' | 'committed' | 'cancelled'; items?: Array<{ sourceLinkId: string; succeeded: boolean; state: string }> }
const clipboardOwner = Symbol('workspace.clipboard');
export function workspaceClipboard(instance: StandardCrudActionInstance | undefined): WorkspaceClipboardSession {
  if (!instance?.active) throw new StandardCrudRequestError('Workspace.ClipboardUnavailable', 409, {}, { code: 'Workspace.ClipboardUnavailable' });
  return instance.getOrCreate(clipboardOwner, () => new WorkspaceClipboardSession(() => instance.changed()), session => session.close());
}

async function command(path: string, body: unknown, featurePosition: string | undefined, signal?: AbortSignal, declarationId?: string): Promise<unknown> {
  const response = await platformFetch(`/api/workspace/content/${path}`, { method: 'POST', signal,
    headers: { 'Content-Type': 'application/json', ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}),
      ...(declarationId ? { 'X-NewP-Data-Declaration': declarationId } : {}) }, body: JSON.stringify(body) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return await response.json() as unknown;
}

/** 只属于当前树实例的内存快照；提交的输入、未知结果与迟到响应由此唯一拥有。 */
export class WorkspaceClipboardSession {
  private state: ClipboardState = { phase: 'idle' };
  private generation = 0;
  private closed = false;
  private validation?: AbortController;
  private validationBase?: ClipboardState;
  private folderSource?: WorkspaceClipboard;
  private objectSource?: { clipboard: WorkspaceClipboard; model: 'Part' | 'Document' };
  private objectOperation?: { id: string; featurePosition?: string; draft: ObjectCopyDraft; receipt?: ObjectCopyReceipt };
  private readonly objectDrafts = new WeakSet<ObjectCopyDraft>();
  private readonly folderDrafts = new WeakSet<FolderCopyDraft>();
  private readonly listeners = new Set<() => void>();
  constructor(private readonly changed: () => void) {}
  readonly getSnapshot = (): ClipboardState => this.state;
  readonly subscribe = (listener: () => void): (() => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };

  copy(sourcePosition: ClipboardPosition, linkIds: readonly string[], featurePosition?: string, declarationId?: string): Promise<boolean> {
    return this.select('copy', sourcePosition, linkIds, featurePosition, declarationId);
  }
  cut(sourcePosition: ClipboardPosition, linkIds: readonly string[], featurePosition?: string, declarationId?: string): Promise<boolean> {
    return this.select('cut', sourcePosition, linkIds, featurePosition, declarationId);
  }
  private async select(mode: 'copy' | 'cut', sourcePosition: ClipboardPosition, linkIds: readonly string[], featurePosition?: string, declarationId?: string): Promise<boolean> {
    if (this.closed || this.state.phase === 'pasting') throw this.failure('Workspace.ClipboardBusy');
    if (linkIds.length < 1 || linkIds.length > 100 || linkIds.some(id => !id) || new Set(linkIds.map(id => id.toLowerCase())).size !== linkIds.length)
      throw this.failure('Workspace.InvalidSelection');
    const clipboard: WorkspaceClipboard = { mode, sourcePosition: { ...sourcePosition, linkIds: [...sourcePosition.linkIds] }, linkIds: [...linkIds] };
    const generation = ++this.generation;
    const previous = this.state.phase === 'validating' ? this.validationBase! : this.state;
    this.validationBase = previous;
    this.validation?.abort(); const validation = new AbortController(); this.validation = validation;
    this.publish({ ...this.state, phase: 'validating' });
    try {
      const receipt = await command(mode === 'cut' ? 'clipboard/cut/validate' : 'clipboard/validate', clipboard, featurePosition, validation.signal, declarationId) as { items?: Array<{ linkId: string; modelKey?: string }> };
      if (this.closed || generation !== this.generation) return false;
      if (!Array.isArray(receipt.items) || receipt.items.length !== linkIds.length || receipt.items.some((item, index) => item.linkId !== linkIds[index]))
        throw this.failure('Workspace.InvalidClipboardInput');
      this.folderSource = mode === 'copy' && receipt.items.length === 1 && receipt.items[0].modelKey === 'WorkspaceFolder' ? clipboard : undefined;
      this.objectSource = mode !== 'copy' ? undefined : receipt.items.length === 1 && receipt.items[0].modelKey === 'Part'
        ? { clipboard, model: 'Part' } : receipt.items.every(item => item.modelKey === 'Document') ? { clipboard, model: 'Document' } : undefined;
      this.publish({ phase: 'idle', clipboard }); return true;
    } catch (failure) {
      if (this.closed || generation !== this.generation) return false;
      this.publish(previous); throw failure;
    } finally { if (this.validation === validation) this.validation = undefined; }
  }

  canCopyFolder(): boolean { return !this.closed && this.state.phase === 'idle' && !!this.folderSource && this.folderSource === this.state.clipboard; }
  canCopyObject(): boolean { return !this.closed && this.state.phase === 'idle' && !!this.objectSource && this.objectSource.clipboard === this.state.clipboard; }
  canResolveObjectCopy(): boolean { return !this.closed && !!this.objectOperation && this.state.phase === 'unknown'; }
  prepareObjectCopy(target: ClipboardPosition, featurePosition?: string, declarationId?: string): ObjectCopyDraft {
    if (!this.canCopyObject() || target.linkIds.length === 0) throw this.failure('Workspace.ObjectCopySourceRequired');
    // Part声明来自实际动作；Document仅由已验证的真实模型进入317前的明确过渡。
    const model = this.objectSource!.model;
    if (!declarationId) throw this.failure('Workspace.CopyInputExpired');
    const draft = { source: this.state.clipboard!, model, declarationId, targetKey: JSON.stringify(target), featurePosition, closed: false };
    this.objectDrafts.add(draft); return draft;
  }
  async readObjectCopyInput(draft: ObjectCopyDraft, signal: AbortSignal): Promise<ObjectCopyPreparation> {
    this.requireObjectDraft(draft);
    const preparation = await command('objects/copy/prepare', { ...draft.source, targetPosition: JSON.parse(draft.targetKey) as ClipboardPosition }, draft.featurePosition, signal, draft.declarationId) as ObjectCopyPreparation;
    this.requireObjectDraft(draft);
    const batch = draft.source.linkIds.length > 1;
    if (!preparation || typeof preparation.operationId !== 'string' || !preparation.values || !Array.isArray(preparation.sourcePath) || !Array.isArray(preparation.targetPath)
      || !batch && (typeof preparation.values.ENAME !== 'string' || preparation.values.ECODE !== '')
      || batch && (preparation.modelKey !== 'Document' || !Array.isArray(preparation.items) || preparation.items.length !== draft.source.linkIds.length
        || preparation.items.some((item, index) => item.sourceLinkId !== draft.source.linkIds[index] || item.values.ECODE !== '' || typeof item.values.ENAME !== 'string')
        || JSON.stringify(preparation.values.items) !== JSON.stringify(preparation.items.map(item => ({ sourceLinkId: item.sourceLinkId, values: item.values })))))
      throw this.failure('Workspace.InvalidClipboardInput');
    draft.operationId = preparation.operationId; draft.requiresReconfirmation = false;
    this.objectOperation = { id: preparation.operationId, featurePosition: draft.featurePosition, draft };
    return preparation;
  }
  async pasteObjectCopy(draft: ObjectCopyDraft, values: Record<string, unknown>, target: ClipboardPosition, featurePosition?: string, declarationId?: string): Promise<ObjectCopyReceipt> {
    this.requireObjectDraft(draft);
    if (draft.requiresReconfirmation) throw this.failure('Workspace.CopySourceChanged');
    if (!this.canCopyObject() || !draft.operationId || draft.targetKey !== JSON.stringify(target) || draft.featurePosition !== featurePosition || draft.declarationId !== declarationId)
      throw this.failure('Workspace.CopyInputExpired');
    const operation = { id: draft.operationId, featurePosition, draft, receipt: undefined as ObjectCopyReceipt | undefined };
    this.objectOperation = operation;
    const clipboard = this.state.clipboard!;
    this.publish({ phase: 'pasting', clipboard });
    try {
      const result = await command('objects/copy', { operationId: draft.operationId, values: { ...values } }, featurePosition, undefined, draft.declarationId) as ObjectCopyReceipt;
      if (result.state !== 'committed' || result.succeeded !== true) throw this.failure('Workspace.CopyOutcomeUnknown');
      draft.closed = true;
      if (this.objectOperation === operation) {
        this.objectOperation = undefined;
        if (this.state.clipboard === clipboard) this.publish({ phase: 'idle', clipboard });
      }
      return result;
    } catch (failure) {
      if (operation.receipt?.state === 'committed') return operation.receipt;
      if (operation.receipt?.state === 'cancelled') {
        throw this.failure('Workspace.CopyCancelled');
      }
      if (failure instanceof StandardCrudRequestError) draft.fieldErrors = failure.fieldErrors;
      const known = this.isKnownFailure(failure, 'Workspace.CopyOutcomeUnknown');
      if (known && failure.code === 'Workspace.CopySourceChanged') draft.requiresReconfirmation = true;
      if (this.objectOperation === operation && this.state.clipboard === clipboard) this.publish({ phase: known ? 'idle' : 'unknown', clipboard });
      throw known ? failure : Object.assign(this.failure('Workspace.CopyOutcomeUnknown'), { cause: failure });
    }
  }
  async resolveObjectCopy(cancel = false, featurePosition?: string): Promise<ObjectCopyReceipt> {
    if (this.closed || !this.objectOperation) throw this.failure('Workspace.CopyOperationUnavailable');
    const operation = this.objectOperation;
    let result: ObjectCopyReceipt;
    try {
      result = await command(`objects/copy/${cancel ? 'cancel' : 'result'}`, { operationId: operation.id }, featurePosition ?? operation.featurePosition) as ObjectCopyReceipt;
      if (!result || !['ready', 'preparing', 'committed', 'cancelled'].includes(result.state) || result.succeeded !== (result.state === 'committed'))
        throw this.failure('Workspace.CopyOutcomeUnknown');
    } catch (failure) {
      const known = this.isKnownFailure(failure, 'Workspace.CopyOutcomeUnknown');
      if (cancel && !known && this.objectOperation === operation && this.state.clipboard === operation.draft.source)
        this.publish({ ...this.state, phase: 'unknown' });
      throw failure;
    }
    if (this.objectOperation === operation) {
      if (result.state === 'committed' || result.state === 'cancelled') {
        operation.receipt = result; operation.draft.closed = true;
        this.objectOperation = undefined;
        this.publish({ ...this.state, phase: 'idle' });
      } else if (this.state.phase !== 'pasting') {
        // preparing只证明尚无提交回执，重启后也不能推断仍有worker；保留核对/取消入口。
        this.publish({ ...this.state, phase: result.state === 'preparing' ? 'unknown' : 'idle' });
      }
    }
    return result;
  }
  private requireObjectDraft(draft: ObjectCopyDraft): void {
    if (this.closed || draft.closed || !this.objectDrafts.has(draft) || this.state.clipboard !== draft.source) throw this.failure('Workspace.CopyInputExpired');
  }
  prepareFolderCopy(target: ClipboardPosition, featurePosition?: string, kind: FolderCopyKind = 'copy', declarationId?: string): FolderCopyDraft {
    if (!declarationId) throw this.failure('Workspace.FolderCopyInputExpired');
    if (!this.canCopyFolder()) throw this.failure('Workspace.FolderCopySourceRequired');
    const draft = { kind, declarationId, source: this.state.clipboard!, targetKey: JSON.stringify(target), featurePosition, closed: false };
    this.folderDrafts.add(draft); return draft;
  }
  async readFolderCopyInput(draft: FolderCopyDraft, signal: AbortSignal): Promise<FolderCopyPreparation> {
    if (this.closed || draft.closed || !this.folderDrafts.has(draft) || this.state.clipboard !== draft.source) throw this.failure('Workspace.FolderCopyInputExpired');
    const receipt = await command(`folders/${draft.kind}/prepare`, { ...draft.source, targetPosition: JSON.parse(draft.targetKey) as ClipboardPosition }, draft.featurePosition, signal, draft.declarationId) as FolderCopyPreparation;
    if (this.closed || draft.closed || this.state.clipboard !== draft.source) throw this.failure('Workspace.FolderCopyInputExpired');
    if (!receipt || typeof receipt.sourceName !== 'string' || !Array.isArray(receipt.sourcePath) || !Array.isArray(receipt.targetPath)
      || receipt.sourcePath.length === 0 || receipt.targetPath.length === 0 || [...receipt.sourcePath, ...receipt.targetPath].some(name => typeof name !== 'string'))
      throw this.failure('Workspace.InvalidClipboardInput');
    return receipt;
  }
  async pasteFolderCopy(draft: FolderCopyDraft, name: string, target: ClipboardPosition, featurePosition?: string, kind: FolderCopyKind = 'copy', copyParentAuthorization?: boolean, declarationId?: string): Promise<ClipboardPasteReceipt> {
    if (!this.canCopyFolder() || draft.closed || !this.folderDrafts.has(draft) || draft.source !== this.state.clipboard
      || !declarationId || draft.declarationId !== declarationId || draft.kind !== kind || draft.targetKey !== JSON.stringify(target) || draft.featurePosition !== featurePosition)
      throw this.failure('Workspace.FolderCopyInputExpired');
    return this.write(`folders/${draft.kind}`, { ...draft.source, targetPosition: JSON.parse(draft.targetKey) as ClipboardPosition, name,
      ...(copyParentAuthorization !== undefined ? { copyParentAuthorization } : {}) }, featurePosition, declarationId);
  }

  async paste(targetPosition: ClipboardPosition, featurePosition?: string, declarationId?: string): Promise<ClipboardPasteReceipt> {
    if (this.closed || this.state.phase !== 'idle' || !this.state.clipboard)
      throw this.failure(this.state.phase === 'unknown' ? 'Workspace.ClipboardOutcomeUnknown' : 'Workspace.ClipboardUnavailable');
    const clipboard = this.state.clipboard;
    return this.write('paste', { ...clipboard, targetPosition: { ...targetPosition, linkIds: [...targetPosition.linkIds] } }, featurePosition, declarationId);
  }

  private async write(path: string, body: unknown, featurePosition?: string, declarationId?: string): Promise<ClipboardPasteReceipt> {
    const clipboard = this.state.clipboard!;
    this.publish({ phase: 'pasting', clipboard });
    try {
      const receipt = await command(path, body, featurePosition, undefined, declarationId);
      if (typeof receipt !== 'object' || receipt === null || !('succeeded' in receipt) || receipt.succeeded !== true
        || 'noChange' in receipt && typeof receipt.noChange !== 'boolean')
        throw this.failure('Workspace.ClipboardOutcomeUnknown');
      if (!this.closed) this.publish({ phase: 'idle', ...(clipboard.mode === 'copy' ? { clipboard } : {}) });
      return receipt as ClipboardPasteReceipt;
    } catch (failure) {
      // 丢失回执、服务器故障或正式未知提交结果均不能恢复可粘贴状态，更不能自动重发。
      const known = this.isKnownFailure(failure, 'Workspace.ClipboardOutcomeUnknown');
      if (!this.closed) this.publish({ phase: known ? 'idle' : 'unknown', clipboard });
      throw known ? failure : Object.assign(this.failure('Workspace.ClipboardOutcomeUnknown'), { cause: failure });
    }
  }

  clear(): boolean {
    if (this.closed || this.state.phase === 'pasting') return false;
    ++this.generation; this.validation?.abort(); this.publish({ phase: 'idle' }); return true;
  }
  close(): void {
    if (this.closed) return;
    this.closed = true; ++this.generation; this.validation?.abort(); this.state = { phase: 'idle' };
    for (const listener of this.listeners) listener();
  }
  private publish(state: ClipboardState): void { if (!this.closed) { this.state = state; this.changed(); for (const listener of this.listeners) listener(); } }
  private isKnownFailure(failure: unknown, outcomeUnknownCode: string): failure is StandardCrudRequestError {
    return failure instanceof StandardCrudRequestError && failure.status >= 400 && failure.status < 500
      && !['Persistence.CommitOutcomeUnknown', 'Platform.MutationOutcomeUnknown', outcomeUnknownCode].includes(failure.code ?? '');
  }
  private failure(code: string): StandardCrudRequestError { return new StandardCrudRequestError(code, 409, {}, { code }); }
}
