import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { Alert, Input } from 'antd';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import { workspaceClipboard, type FolderCopyDraft, type FolderCopyPreparation } from './workspaceClipboard';
import { workspacePosition } from './workspacePosition';
import WorkspaceParentAuthorizationOption from './WorkspaceParentAuthorizationOption';

const noSubscription = () => () => undefined;
const noSnapshot = () => undefined;

/** 挂载时固定实例快照和目标，关闭或输入变化后旧draft不能再次发出写入。 */
export default function WorkspaceFolderCopyInput({ instance, action, queryScope, featurePosition, value, onChange, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const callbacks = useRef({ onChange, onDraftStateChange }); callbacks.current = { onChange, onDraftStateChange };
  const [draft, setDraft] = useState<FolderCopyDraft>();
  const [failure, setFailure] = useState(false);
  const [preparation, setPreparation] = useState<FolderCopyPreparation>();
  const session = useMemo(() => instance?.active ? workspaceClipboard(instance) : undefined, [instance]);
  const state = useSyncExternalStore(session?.subscribe ?? noSubscription, session?.getSnapshot ?? noSnapshot);
  const name = typeof value === 'object' && value !== null && 'name' in value && typeof value.name === 'string' ? value.name : '';
  const kind = action.key === 'pasteFolderStructure' ? 'copy-structure' : 'copy';
  const root = workspacePosition(queryScope).linkIds.length === 0;
  const copyParentAuthorization = !root && !(typeof value === 'object' && value !== null && 'copyParentAuthorization' in value && value.copyParentAuthorization === false);
  const targetKey = JSON.stringify(workspacePosition(queryScope));
  const expired = failure || !!draft && (draft.closed || !instance?.active || state?.clipboard !== draft.source);
  useEffect(() => {
    let current: FolderCopyDraft | undefined;
    const read = new AbortController();
    setPreparation(undefined);
    try {
      const owner = workspaceClipboard(instance);
      current = owner.prepareFolderCopy(JSON.parse(targetKey), featurePosition, kind);
      setDraft(current); setFailure(false); callbacks.current.onChange({ draft: current, name: '', copyParentAuthorization: !root });
      const opened = current;
      void owner.readFolderCopyInput(opened, read.signal).then(result => {
        if (read.signal.aborted || opened.closed) return;
        setPreparation(result); callbacks.current.onChange({ draft: opened, name: result.sourceName, copyParentAuthorization: !root });
      }).catch(() => { if (!read.signal.aborted && !opened.closed) setFailure(true); });
    } catch { setFailure(true); }
    return () => { if (current) current.closed = true; read.abort(); };
  }, [instance, targetKey, featurePosition, kind, root]);
  useEffect(() => {
    callbacks.current.onDraftStateChange?.({ dirty: !!preparation && (name !== preparation.sourceName || !root && !copyParentAuthorization), ready: !!draft && !!preparation && !expired && state?.phase === 'idle' && !submitting && name.trim().length > 0 && name.length <= 256 });
  }, [draft, preparation, expired, state?.phase, name, submitting, kind, root, copyParentAuthorization]);
  return <div style={{ display: 'grid', gap: 16 }}>
    <Alert type={expired ? 'error' : 'info'} showIcon message={t(expired ? 'Workspace.FolderCopyInputExpired' : kind === 'copy-structure' ? 'Workspace.FolderStructureOnly' : 'Workspace.FolderCopyShared')} />
    {preparation ? <dl style={{ display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr)', gap: '8px 16px', margin: 0 }}>
      <dt>{t('Workspace.FolderCopySource')}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }}>{preparation.sourcePath.join(' / ')}</dd>
      <dt>{t('Workspace.FolderCopyTarget')}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }}>{preparation.targetPath.join(' / ')}</dd>
    </dl> : !expired ? <div role="status">{t('Workspace.FolderCopyLoading')}</div> : null}
    <p style={{ margin: 0 }}>{t('Workspace.FolderCopyLiveContents')}</p>
    <label htmlFor="workspace-copy-name">{t('Workspace.FolderCopyName')}</label>
    <Input id="workspace-copy-name" value={name} maxLength={256} disabled={submitting || expired || !preparation || state?.phase !== 'idle'} autoFocus
      onChange={event => onChange({ draft, name: event.target.value, copyParentAuthorization })} />
    <WorkspaceParentAuthorizationOption root={root} checked={copyParentAuthorization}
      disabled={submitting || expired || !preparation || state?.phase !== 'idle'}
      onChange={checked => onChange({ draft, name, copyParentAuthorization: checked })} />
    {kind === 'copy-structure' ? <p style={{ margin: 0 }}>{t('Workspace.FolderStructureParentAuthorization')}</p> : null}
  </div>;
}
