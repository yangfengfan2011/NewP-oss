import React from 'react';
import { Modal } from './Modal';

export interface ConfirmDialogProps {
  open: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  title?: string;
  message: string;
  description?: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'default' | 'danger';
}

export function ConfirmDialog({
  open,
  onConfirm,
  onCancel,
  title = '确认',
  message,
  description,
  confirmText = '确认',
  cancelText = '取消',
  variant = 'default',
}: ConfirmDialogProps) {
  const confirmClass = variant === 'danger' ? 'sh-btn sh-btn-danger' : 'sh-btn sh-btn-primary';

  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="sm"
      footer={
        <>
          <button className="sh-btn" onClick={onCancel}>
            {cancelText}
          </button>
          <button className={confirmClass} onClick={onConfirm}>
            {confirmText}
          </button>
        </>
      }
    >
      <div className="sh-confirm-body">
        <p className="sh-confirm-message">{message}</p>
        {description && <p className="sh-confirm-description">{description}</p>}
      </div>
    </Modal>
  );
}
