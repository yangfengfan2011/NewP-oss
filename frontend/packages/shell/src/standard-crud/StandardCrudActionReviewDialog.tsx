import { useLocalization } from '@icp-platform/core';
import { StandardCrudDialogTitle } from './StandardCrudDialogTitle';
import { lazy, Suspense, useEffect, useRef, useState, type ReactElement, type ReactNode } from 'react';
import { useOptionalAuth } from '../contexts/AuthContext';
import { useFeaturePlacement } from '../authorization/FeaturePlacementContext';
import Modal from 'antd/es/modal';
import Button from 'antd/es/button';
import type {
  StandardCrudActionDefinition,
  StandardCrudActionInstance,
  StandardCrudActionInputRenderer,
  StandardCrudListQueryRequest,
} from '@icp-platform/sdk';

const ActionFormDrawer = lazy(() => import('@icp-platform/ui/crud-form').then(module => ({ default: module.CrudFormDrawer })));

interface StandardCrudActionReviewDialogProps {
  dataAccessDeclarations?: readonly import('@icp-platform/sdk').ActionDataAccessRequirement[];
  instance?: StandardCrudActionInstance;
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
  selectedRows?: readonly unknown[];
  listQuery?: Readonly<StandardCrudListQueryRequest>;
  pending: boolean;
  error?: string | null;
  submissionFailure?: unknown;
  onChange: (value: unknown) => void;
  onCancel: () => void;
  onConfirm: () => void;
}

/** 动作输入共用生命周期；表单可采用标准 Drawer，关闭前仍保护未保存修改。 */
export function StandardCrudActionReviewDialog({
  dataAccessDeclarations,
  open = true,
  action,
  title,
  confirmMessage,
  inputRenderer,
  instance,
  featurePosition,
  queryScope,
  value,
  detail,
  row,
  selectedRows,
  listQuery,
  pending,
  error,
  submissionFailure,
  onChange,
  onCancel,
  onConfirm,
}: StandardCrudActionReviewDialogProps): ReactElement | null {
  const { t } = useLocalization();
  const auth = useOptionalAuth();
  const features = useFeaturePlacement();
  const authority = JSON.stringify([auth?.user?.id, auth?.isAuthenticated, auth?.isInitialized,
    auth?.permissionEpoch, auth?.user?.isLocalAdministrator, auth?.user?.mustChangePassword,
    [...(auth?.user?.featurePositions ?? [])].sort(), featurePosition, action.key, queryScope]);
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
  const requestClose = () => { if (!pending) { if (draftState.dirty) setConfirmDiscard(true); else onCancel(); } };
  const content = <>
    {confirmMessage ? <p>{confirmMessage}</p> : null}
    {inputRenderer({
      action, featurePosition, instance, buttonId: action.buttonId,
      dataAccess: action.dataAccess, dataAccessDeclarations,
      navigationFeaturePosition: key => {
        const resolution = features.navigation(key);
        return resolution.kind === 'allowed' ? resolution.position : undefined;
      },
      queryScope, submitting: pending, submissionFailure, value, onChange, detail, row, selectedRows, listQuery, onDraftStateChange: setDraftState,
    })}
  </>;
  return (
    <>
      {inputRenderer.presentation === 'drawer' ? <Suspense fallback={null}>
        <ActionFormDrawer open={open} title={<StandardCrudDialogTitle>{title}</StandardCrudDialogTitle>}
          saving={pending} error={error} hidePrimary={inputRenderer.readOnly}
          primaryLabel={action.confirmLabel ?? t('Platform.SaveAndClose', '保存并关闭')}
          primaryDisabled={!draftState.ready || pending} primaryAction={onConfirm} onClose={requestClose}>
          {content}
        </ActionFormDrawer>
      </Suspense> : <Modal
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
        onCancel={requestClose}
        onOk={inputRenderer.readOnly ? onCancel : onConfirm}
      >
        {error ? <div role="alert">{error}</div> : null}
        {content}
      </Modal>}
      <Modal open={confirmDiscard} title={<StandardCrudDialogTitle>{t('Platform.DiscardDraftTitle', '放弃未保存的草稿？')}</StandardCrudDialogTitle>} okText={t('Platform.DiscardDraft', '放弃修改')} cancelText={t('Platform.KeepEditing', '继续编辑')}
        closable={{ 'aria-label': t('Platform.Close', '关闭') }}
        onCancel={() => setConfirmDiscard(false)} onOk={() => { setConfirmDiscard(false); onCancel(); }}>
        {t('Platform.UnsavedChanges', '当前修改尚未保存。')}
      </Modal>
    </>
  );
}
