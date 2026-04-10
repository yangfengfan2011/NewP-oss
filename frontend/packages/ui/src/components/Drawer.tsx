import React, { useEffect, useCallback } from 'react';
import ReactDOM from 'react-dom';

export interface DrawerProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  size?: 'sm' | 'default' | 'lg';
  footer?: React.ReactNode;
  children: React.ReactNode;
}

export function Drawer({ open, onClose, title, size = 'default', footer, children }: DrawerProps) {
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

  const sizeClass = size !== 'default' ? ` sh-drawer-${size}` : '';

  return ReactDOM.createPortal(
    <div className="sh-drawer-overlay" onClick={onClose} role="presentation">
      <div
        className={`sh-drawer${sizeClass}`}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        {title && (
          <div className="sh-drawer-header">
            <h2 className="sh-drawer-title">{title}</h2>
            <button className="sh-drawer-close" onClick={onClose} aria-label="关闭">
              ×
            </button>
          </div>
        )}
        <div className="sh-drawer-body">{children}</div>
        {footer && <div className="sh-drawer-footer">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}
