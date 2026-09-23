import { useState, type ReactElement, type ReactNode } from 'react';
import Modal from 'antd/es/modal';
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
  value,
  detail,
  row,
  pending,
  onChange,
  onCancel,
  onConfirm,
}: StandardCrudActionReviewDialogProps): ReactElement {
  const [draftState, setDraftState] = useState({ dirty: false, ready: true });
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  return (
    <>
      <Modal
        open={open}
        title={title}
        okText={action.confirmLabel ?? '确认'}
        cancelText="取消"
        destroyOnHidden
        confirmLoading={pending}
        okButtonProps={{ disabled: !draftState.ready }}
        onCancel={() => { if (!pending) { if (draftState.dirty) setConfirmDiscard(true); else onCancel(); } }}
        onOk={onConfirm}
      >
        {confirmMessage ? <p>{confirmMessage}</p> : null}
        {inputRenderer({
          action,
          featurePosition,
          value,
          onChange,
          detail,
          row,
          onDraftStateChange: setDraftState,
        })}
      </Modal>
      <Modal open={confirmDiscard} title="放弃未保存的草稿？" okText="放弃修改" cancelText="继续编辑"
        onCancel={() => setConfirmDiscard(false)} onOk={() => { setConfirmDiscard(false); onCancel(); }}>
        当前修改尚未保存。
      </Modal>
    </>
  );
}
