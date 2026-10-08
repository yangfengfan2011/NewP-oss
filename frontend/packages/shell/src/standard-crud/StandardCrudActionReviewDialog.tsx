import { useLocalization } from '@icp-platform/core';
import { StandardCrudDialogTitle } from './StandardCrudDialogTitle';
import { useEffect, useRef, useState, type ReactElement, type ReactNode } from 'react';
import { useOptionalAuth } from '../contexts/AuthContext';
import Modal from 'antd/es/modal';
import Button from 'antd/es/button';
import type {
  StandardCrudActionDefinition,
  StandardCrudActionInputRenderer,
} from '@icp-platform/sdk';

interface StandardCrudActionReviewDialogProps {
  open?: boolean;
  action: StandardCrudActionDefinition;
  title: ReactNode;
  confirmMessage?: string;
  inputRenderer: StandardCrudActionInputRenderer;
  featurePosition?: string;
  queryScope?: Record<string, unknown>;
  value: unknown;
  detail?: unknown;
  row?: unknown;
  pending: boolean;
  onChange: (value: unknown) => void;
  onCancel: () => void;
  onConfirm: () => void;
}

/** 带业务输入的动作共用 Review Dialog；输入报告草稿状态后，关闭前保护未保存修改。 */
export function StandardCrudActionReviewDialog({
  open = true,
  action,
  title,
  confirmMessage,
  inputRenderer,
  featurePosition,
  queryScope,
  value,
  detail,
  row,
  pending,
  onChange,
  onCancel,
  onConfirm,
}: StandardCrudActionReviewDialogProps): ReactElement | null {
  const { t } = useLocalization();
  const auth = useOptionalAuth();
  const authority = JSON.stringify([auth?.user?.id, auth?.isAuthenticated, auth?.isInitialized,
    auth?.permissionEpoch, auth?.user?.isLocalAdministrator, auth?.user?.mustChangePassword,
    [...(auth?.user?.featurePositions ?? [])].sort(), featurePosition, action.key]);
  const [openedAuthority] = useState(authority);
  const [invalidated, setInvalidated] = useState(false);
  const cancelReported = useRef(false);
  const authorityChanged = invalidated || authority !== openedAuthority;
  // 语言不属于授权边界。边界变化立即卸载旧输入且不复活，已发写入仍由原执行器完成。
  useEffect(() => {
    if (!authorityChanged) return;
    setInvalidated(true);
    if (!pending && !cancelReported.current) {
      cancelReported.current = true;
      onCancel();
    }
  }, [authorityChanged, pending, onCancel]);
  const [draftState, setDraftState] = useState({ dirty: false, ready: true });
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  if (authorityChanged) return null;
  return (
    <>
      <Modal
        closable={{ 'aria-label': t('Platform.Close', '关闭') }}
        open={open}
        title={<StandardCrudDialogTitle>{title}</StandardCrudDialogTitle>}
        width={inputRenderer.dialogWidth}
        footer={inputRenderer.readOnly ? <Button onClick={onCancel}>{t('Platform.Close', '关闭')}</Button> : undefined}
        okText={action.confirmLabel ?? t('Platform.Confirm', '确认')}
        cancelText={t('Platform.Cancel', '取消')}
        destroyOnHidden
        confirmLoading={pending}
        okButtonProps={{ disabled: !draftState.ready }}
        onCancel={() => { if (!pending) { if (draftState.dirty) setConfirmDiscard(true); else onCancel(); } }}
        onOk={inputRenderer.readOnly ? onCancel : onConfirm}
      >
        {confirmMessage ? <p>{confirmMessage}</p> : null}
        {inputRenderer({
          action,
          featurePosition,
          queryScope,
          submitting: pending,
          value,
          onChange,
          detail,
          row,
          onDraftStateChange: setDraftState,
        })}
      </Modal>
      <Modal open={confirmDiscard} title={<StandardCrudDialogTitle>{t('Platform.DiscardDraftTitle', '放弃未保存的草稿？')}</StandardCrudDialogTitle>} okText={t('Platform.DiscardDraft', '放弃修改')} cancelText={t('Platform.KeepEditing', '继续编辑')}
        closable={{ 'aria-label': t('Platform.Close', '关闭') }}
        onCancel={() => setConfirmDiscard(false)} onOk={() => { setConfirmDiscard(false); onCancel(); }}>
        {t('Platform.UnsavedChanges', '当前修改尚未保存。')}
      </Modal>
    </>
  );
}
