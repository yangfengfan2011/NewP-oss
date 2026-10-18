import { platformFetch } from '@icp-platform/core';
import { isStandardCrudRecord, readStandardCrudRequestError, StandardCrudRequestError,
  type StandardCrudActionContext, type StandardCrudActionInstance, type StandardCrudOperationResult } from '@icp-platform/sdk';
import type { DataScopeSubject, SubjectType } from '@icp-platform/ui';
import { workspacePosition } from './workspacePosition';

export interface BatchSource { position: ReturnType<typeof workspacePosition>; linkIds: string[] }
export interface BatchItem { resourceKey: string; objectId: string; name: string; code: string | null; manageable: boolean; source: BatchSource }
export interface BatchCatalog { resourceKey: string; actions: { key: string; title: string; enabled: boolean }[]; subjectTypes: SubjectType[] }
export interface BatchCollection { items: BatchItem[]; total: number; catalogs: BatchCatalog[] }
export interface BatchCurrentConfiguration { resourceKey: string; objectId: string; subjects: DataScopeSubject[] }
export interface BatchDraft { catalogs: BatchCatalog[]; mode: 'U' | 'R'; confirmClear: boolean; items: BatchItem[];
  resources: { resourceKey: string; subjects: DataScopeSubject[] }[] }
export interface BatchFilters { resourceKeys?: string[]; createdBy?: string; ownerId?: string }

class BatchSubmission {
  private unknown = false;
  private disposed = false;
  private pending?: AbortController;
  private readonly listeners = new Set<() => void>();
  lastDraft?: BatchDraft;
  readonly getSnapshot = () => this.unknown;
  readonly subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  freeze(draft: BatchDraft) {
    if (this.disposed) return;
    this.lastDraft = structuredClone(draft);
    this.unknown = true;
    for (const listener of this.listeners) listener();
  }
  restart() {
    if (this.disposed || this.pending) return;
    this.lastDraft = undefined; this.unknown = false;
    for (const listener of this.listeners) listener();
  }
  begin() {
    if (this.disposed || this.pending) throw new StandardCrudRequestError('Workspace.BatchBusy', 409);
    this.pending = new AbortController();
    return this.pending;
  }
  finish(controller: AbortController) { if (this.pending === controller) this.pending = undefined; }
  dispose() { this.disposed = true; this.pending?.abort(); this.pending = undefined; this.lastDraft = undefined; this.listeners.clear(); }
}
const submissionOwner = Symbol('workspace.batchAuthorization');
export function batchSubmission(instance?: StandardCrudActionInstance) {
  if (!instance?.active) return undefined;
  return instance.getOrCreate(submissionOwner, () => new BatchSubmission(), value => value.dispose());
}

export function batchInitialSources(context: Pick<StandardCrudActionContext, 'selectedRows' | 'queryScope'>): BatchSource[] {
  const links = context.selectedRows?.map(row => isStandardCrudRecord(row) && typeof row.LID === 'string' ? row.LID : '') ?? [];
  if (!links.length || links.some(link => !link)) throw new StandardCrudRequestError('Workspace.InvalidSelection', 400);
  return [{ position: workspacePosition(context.queryScope), linkIds: [...new Set(links)] }];
}

/** API是完整集合；所有对象身份和来源按白名单读取，不把任意响应传播到提交草稿。 */
export function decodeBatchCollection(value: unknown): BatchCollection {
  const invalid = () => new StandardCrudRequestError('Workspace.BatchInvalidResponse', 502);
  if (!isStandardCrudRecord(value) || !Array.isArray(value.items) || !Array.isArray(value.catalogs) || value.total !== value.items.length) throw invalid();
  const items = value.items.map((item): BatchItem => {
    if (!isStandardCrudRecord(item) || typeof item.resourceKey !== 'string' || typeof item.objectId !== 'string'
      || typeof item.name !== 'string' || item.code !== null && typeof item.code !== 'string' || typeof item.manageable !== 'boolean'
      || !isStandardCrudRecord(item.source) || !isStandardCrudRecord(item.source.position)
      || typeof item.source.position.spaceId !== 'string' || typeof item.source.position.rootFolderId !== 'string'
      || !Array.isArray(item.source.position.linkIds) || item.source.position.linkIds.some(id => typeof id !== 'string')
      || !Array.isArray(item.source.linkIds) || item.source.linkIds.length !== 1 || typeof item.source.linkIds[0] !== 'string') throw invalid();
    return { resourceKey: item.resourceKey, objectId: item.objectId, name: item.name, code: item.code, manageable: item.manageable,
      source: { position: { spaceId: item.source.position.spaceId, rootFolderId: item.source.position.rootFolderId,
        linkIds: [...item.source.position.linkIds] as string[] }, linkIds: [item.source.linkIds[0]] } };
  });
  const catalogs = value.catalogs.map((catalog): BatchCatalog => {
    if (!isStandardCrudRecord(catalog) || typeof catalog.resourceKey !== 'string' || !Array.isArray(catalog.actions) || !Array.isArray(catalog.subjectTypes)) throw invalid();
    return { resourceKey: catalog.resourceKey, actions: catalog.actions.map(action => {
      if (!isStandardCrudRecord(action) || typeof action.key !== 'string' || typeof action.title !== 'string' || typeof action.enabled !== 'boolean') throw invalid();
      return { key: action.key, title: action.title, enabled: action.enabled };
    }), subjectTypes: catalog.subjectTypes.map(type => {
      if (!isStandardCrudRecord(type) || typeof type.key !== 'string' || typeof type.title !== 'string' || typeof type.requiresId !== 'boolean') throw invalid();
      return { key: type.key, title: type.title, requiresId: type.requiresId, supportsDescendants: type.supportsDescendants === true };
    }) };
  });
  if (new Set(items.map(item => batchItemKey(item))).size !== items.length
    || new Set(catalogs.map(catalog => catalog.resourceKey)).size !== catalogs.length
    || items.some(item => !catalogs.some(catalog => catalog.resourceKey === item.resourceKey))) throw invalid();
  return { items, total: items.length, catalogs };
}
export const batchItemKey = (item: Pick<BatchItem, 'resourceKey' | 'objectId'>) => JSON.stringify([item.resourceKey, item.objectId]);

export async function prepareBatch(sources: BatchSource[], featurePosition: string, signal: AbortSignal,
  includeDescendants = false, filters?: BatchFilters): Promise<BatchCollection> {
  const response = await platformFetch('/api/workspace/content/permissions/batch/prepare', { method: 'POST', signal,
    headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': featurePosition }, body: JSON.stringify({ sources, includeDescendants, filters }) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  const value: unknown = await response.json();
  signal.throwIfAborted();
  return decodeBatchCollection(value);
}

/** 已发送写请求的稳定未知标识不能作为可安全重试的普通409。 */
export function batchOutcomeUnknown(failure: unknown): boolean {
  return !(failure instanceof StandardCrudRequestError) || failure.status === 0 || failure.status >= 500
    || failure.code === 'Persistence.CommitOutcomeUnknown' || failure.code === 'Platform.MutationOutcomeUnknown'
    || failure.reasonKey === 'AuthorizationMessages.OperationUnknown';
}

/** 核对只读当前整组配置；精确目标匹配，不把任何返回值解释成原请求的提交结论。 */
export async function readBatchCurrent(draft: BatchDraft, featurePosition: string, signal: AbortSignal): Promise<BatchCurrentConfiguration[]> {
  const response = await platformFetch('/api/workspace/content/permissions/batch/current', { method: 'POST', signal,
    headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': featurePosition },
    body: JSON.stringify({ sources: draft.items.map(item => item.source) }) });
  signal.throwIfAborted();
  if (!response.ok) throw await readStandardCrudRequestError(response);
  const body: unknown = await response.json();
  signal.throwIfAborted();
  const invalid = () => new StandardCrudRequestError('Workspace.BatchInvalidResponse', 502);
  if (!Array.isArray(body) || body.length !== draft.items.length) throw invalid();
  const seen = new Set<string>();
  return body.map(configuration => {
    if (!isStandardCrudRecord(configuration) || typeof configuration.resourceKey !== 'string' || typeof configuration.objectId !== 'string'
      || !Array.isArray(configuration.subjects)) throw invalid();
    const key = batchItemKey({ resourceKey: configuration.resourceKey, objectId: configuration.objectId });
    if (seen.has(key) || !draft.items.some(item => batchItemKey(item) === key)) throw invalid();
    seen.add(key);
    const subjects = configuration.subjects.map((subject): DataScopeSubject => {
      if (!isStandardCrudRecord(subject) || typeof subject.id !== 'string' || typeof subject.type !== 'string'
        || subject.subjectId !== null && typeof subject.subjectId !== 'string' || typeof subject.priority !== 'number' || !Number.isInteger(subject.priority)
        || !isStandardCrudRecord(subject.actions) || subject.expiresAt != null && typeof subject.expiresAt !== 'string'
        || subject.matchOptions != null && (!isStandardCrudRecord(subject.matchOptions) || typeof subject.matchOptions.includeDescendants !== 'boolean')) throw invalid();
      const actions: DataScopeSubject['actions'] = {};
      for (const [action, opinion] of Object.entries(subject.actions)) {
        if (!['allow', 'deny', 'unset'].includes(String(opinion))) throw invalid();
        actions[action] = opinion as 'allow' | 'deny' | 'unset';
      }
      return { id: subject.id, type: subject.type, subjectId: subject.subjectId, priority: subject.priority, actions,
        expiresAt: typeof subject.expiresAt === 'string' ? subject.expiresAt : null,
        ...(isStandardCrudRecord(subject.matchOptions) ? { matchOptions: { includeDescendants: subject.matchOptions.includeDescendants === true } } : {}) };
    });
    return { resourceKey: configuration.resourceKey, objectId: configuration.objectId, subjects };
  });
}

/** 同一标准动作只发一个批请求；成功只消费回执，不再读取可能已失权的ACL。 */
export async function saveBatch(context: StandardCrudActionContext): Promise<StandardCrudOperationResult> {
  const draft = context.input as BatchDraft | undefined;
  const submission = batchSubmission(context.instance);
  if (!submission || !context.featurePosition || !draft?.items.length || draft.items.some(item => !item.manageable))
    throw new StandardCrudRequestError('Workspace.InvalidSelection', 400);
  if (submission.getSnapshot()) throw new StandardCrudRequestError('Workspace.BatchUnknown', 0, {}, { code: 'Workspace.BatchUnknown', reasonKey: 'Workspace.BatchUnknown' });
  const controller = submission.begin();
  try {
    const response = await platformFetch('/api/workspace/content/permissions/batch', { method: 'POST', signal: controller.signal,
      headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': context.featurePosition }, body: JSON.stringify({ mode: draft.mode,
        confirmClear: draft.confirmClear, sources: draft.items.map(item => item.source), resources: draft.resources.map(resource => ({
          resourceKey: resource.resourceKey, subjects: resource.subjects.map(subject => ({ type: subject.type, subjectId: subject.subjectId, priority: subject.priority,
            actions: subject.actions, matchOptions: subject.matchOptions, expiresAt: subject.expiresAt })) })) }) });
    controller.signal.throwIfAborted();
    if (!response.ok) throw await readStandardCrudRequestError(response);
    const receipt: unknown = await response.json();
    controller.signal.throwIfAborted();
    if (response.status !== 200 || !isStandardCrudRecord(receipt) || receipt.succeeded !== true || typeof receipt.targets !== 'number' || receipt.targets !== draft.items.length
      || typeof receipt.changed !== 'number' || !Number.isInteger(receipt.changed) || receipt.changed < 0 || receipt.changed > receipt.targets)
      throw new StandardCrudRequestError('Workspace.BatchUnknown', 0, {}, { code: 'Workspace.BatchUnknown', reasonKey: 'Workspace.BatchUnknown' });
    return { operation: 'action', payload: receipt, ...(receipt.changed === 0 ? { noChange: true as const } : { change: { operation: 'update' as const, id: '' } }),
      notice: { reasonKey: 'Workspace.BatchSaved' } };
  } catch (failure) {
    if (batchOutcomeUnknown(failure)) {
      submission.freeze(draft); context.instance?.changed();
    }
    throw failure;
  } finally { submission.finish(controller); }
}
