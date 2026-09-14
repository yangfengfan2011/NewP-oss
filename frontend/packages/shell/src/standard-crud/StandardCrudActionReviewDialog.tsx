import type { ReactElement, ReactNode } from 'react';
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

/** 带业务输入的动作共用一个 Review Dialog，确认文案和不透明输入不会拆成两次确认。 */
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
  return (
    <Modal
      open={open}
      title={title}
      okText="确认"
      cancelText="取消"
      destroyOnHidden
      confirmLoading={pending}
      onCancel={onCancel}
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
      })}
    </Modal>
  );
}
