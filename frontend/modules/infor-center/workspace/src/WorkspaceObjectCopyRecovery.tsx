import { useEffect, useRef, useState } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import Alert from 'antd/es/alert';
import Button from 'antd/es/button';
import { workspaceClipboard, type ObjectCopyReceipt } from './workspaceClipboard';

/** 恢复只读取持久结果或取消准备；打开和重试均不会重发业务创建。 */
export default function WorkspaceObjectCopyRecovery({ instance, featurePosition, onChange, onDraftStateChange }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const [receipt, setReceipt] = useState<ObjectCopyReceipt>();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const alive = useRef(true);
  const request = useRef(0);
  const callbacks = useRef({ onChange, onDraftStateChange }); callbacks.current = { onChange, onDraftStateChange };
  async function read(cancel = false) {
    const current = ++request.current;
    setBusy(true); setFailed(false);
    try {
      const result = await workspaceClipboard(instance).resolveObjectCopy(cancel, cancel ? undefined : featurePosition);
      if (alive.current && current === request.current) {
        setReceipt(result); callbacks.current.onChange(result);
        callbacks.current.onDraftStateChange?.({ dirty: false, ready: result.state === 'committed' || result.state === 'cancelled' });
      }
    } catch { if (alive.current && current === request.current) setFailed(true); }
    finally { if (alive.current && current === request.current) setBusy(false); }
  }
  useEffect(() => {
    alive.current = true; setReceipt(undefined); callbacks.current.onDraftStateChange?.({ dirty: false, ready: false }); void read();
    // Effect重挂载的旧读取不能覆盖后来确认的取消/提交事实。
    return () => { alive.current = false; ++request.current; };
  }, [instance]);
  const terminal = receipt?.state === 'committed' || receipt?.state === 'cancelled';
  return <div style={{ display: 'grid', gap: 16 }}>
    <Alert showIcon type={receipt?.succeeded ? 'success' : 'info'} message={t(failed ? 'Workspace.CopyOutcomeUnknown'
      : receipt?.state === 'committed' ? 'Workspace.ObjectCopied' : receipt?.state === 'cancelled' ? 'Workspace.CopyCancelled'
        : receipt?.state === 'ready' ? 'Workspace.CopyNotCommitted' : 'Workspace.CopyPreparing')} />
    <Button disabled={busy || terminal} loading={busy} onClick={() => void read()}>{t('Workspace.CopyResolve')}</Button>
    <Button disabled={busy || terminal} onClick={() => void read(true)}>{t('Workspace.CopyCancel')}</Button>
  </div>;
}
