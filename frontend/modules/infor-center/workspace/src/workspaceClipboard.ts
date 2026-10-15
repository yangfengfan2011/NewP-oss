import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError } from '@icp-platform/sdk';

export interface ClipboardPosition { spaceId: string; rootFolderId: string; linkIds: readonly string[] }
export interface WorkspaceClipboard { readonly mode: 'copy' | 'cut'; readonly sourcePosition: ClipboardPosition; readonly linkIds: readonly string[] }
export interface ClipboardPasteReceipt { readonly succeeded: true; readonly noChange?: boolean }
export interface ClipboardState { readonly phase: 'idle' | 'validating' | 'pasting' | 'unknown'; readonly clipboard?: WorkspaceClipboard }

async function command(path: string, body: unknown, featurePosition: string | undefined, signal?: AbortSignal): Promise<unknown> {
  const response = await platformFetch(`/api/workspace/content/${path}`, { method: 'POST', signal,
    headers: { 'Content-Type': 'application/json', ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}) }, body: JSON.stringify(body) });
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
  constructor(private readonly changed: () => void) {}
  getSnapshot(): ClipboardState { return this.state; }

  copy(sourcePosition: ClipboardPosition, linkIds: readonly string[], featurePosition?: string): Promise<boolean> {
    return this.select('copy', sourcePosition, linkIds, featurePosition);
  }
  cut(sourcePosition: ClipboardPosition, linkIds: readonly string[], featurePosition?: string): Promise<boolean> {
    return this.select('cut', sourcePosition, linkIds, featurePosition);
  }
  private async select(mode: 'copy' | 'cut', sourcePosition: ClipboardPosition, linkIds: readonly string[], featurePosition?: string): Promise<boolean> {
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
      const receipt = await command(mode === 'cut' ? 'clipboard/cut/validate' : 'clipboard/validate', clipboard, featurePosition, validation.signal) as { items?: Array<{ linkId: string }> };
      if (this.closed || generation !== this.generation) return false;
      if (!Array.isArray(receipt.items) || receipt.items.length !== linkIds.length || receipt.items.some((item, index) => item.linkId !== linkIds[index]))
        throw this.failure('Workspace.InvalidClipboardInput');
      this.publish({ phase: 'idle', clipboard }); return true;
    } catch (failure) {
      if (this.closed || generation !== this.generation) return false;
      this.publish(previous); throw failure;
    } finally { if (this.validation === validation) this.validation = undefined; }
  }

  async paste(targetPosition: ClipboardPosition, featurePosition?: string): Promise<ClipboardPasteReceipt> {
    if (this.closed || this.state.phase !== 'idle' || !this.state.clipboard)
      throw this.failure(this.state.phase === 'unknown' ? 'Workspace.ClipboardOutcomeUnknown' : 'Workspace.ClipboardUnavailable');
    const clipboard = this.state.clipboard;
    this.publish({ phase: 'pasting', clipboard });
    try {
      const receipt = await command('paste', { ...clipboard, targetPosition: { ...targetPosition, linkIds: [...targetPosition.linkIds] } }, featurePosition);
      if (typeof receipt !== 'object' || receipt === null || !('succeeded' in receipt) || receipt.succeeded !== true
        || 'noChange' in receipt && typeof receipt.noChange !== 'boolean')
        throw this.failure('Workspace.ClipboardOutcomeUnknown');
      if (!this.closed) this.publish({ phase: 'idle', ...(clipboard.mode === 'copy' ? { clipboard } : {}) });
      return receipt as ClipboardPasteReceipt;
    } catch (failure) {
      // 丢失回执、服务器故障或正式未知提交结果均不能恢复可粘贴状态，更不能自动重发。
      const known = failure instanceof StandardCrudRequestError && failure.status >= 400 && failure.status < 500
        && !['Platform.MutationOutcomeUnknown', 'Persistence.CommitOutcomeUnknown', 'Workspace.ClipboardOutcomeUnknown'].includes(failure.code ?? '');
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
  }
  private publish(state: ClipboardState): void { if (!this.closed) { this.state = state; this.changed(); } }
  private failure(code: string): StandardCrudRequestError { return new StandardCrudRequestError(code, 409, {}, { code }); }
}
