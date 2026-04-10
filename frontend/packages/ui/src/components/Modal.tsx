import React, { useEffect, useCallback } from 'react';
import ReactDOM from 'react-dom';

export interface ModalProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  size?: 'sm' | 'default' | 'lg';
  footer?: React.ReactNode;
  children: React.ReactNode;
}

export function Modal({ open, onClose, title, size = 'default', footer, children }: ModalProps) {
  const handleKeyDown = useCallback(
    (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    },
    [onClose],
  );

  useEffect(() => {
    if (!open) return;
    document.addEventListener('keydown', handleKeyDown);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      document.body.style.overflow = '';
    };
  }, [open, handleKeyDown]);

  if (!open) return null;

  const sizeClass = size !== 'default' ? ` sh-modal-${size}` : '';

  return ReactDOM.createPortal(
    <div className="sh-overlay" onClick={onClose} role="presentation">
      <div
        className={`sh-modal${sizeClass}`}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        {title && (
          <div className="sh-modal-header">
            <h2 className="sh-modal-title">{title}</h2>
            <button className="sh-modal-close" onClick={onClose} aria-label="关闭">
              ×
            </button>
          </div>
        )}
        <div className="sh-modal-body">{children}</div>
        {footer && <div className="sh-modal-footer">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}
