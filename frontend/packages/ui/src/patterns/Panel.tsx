import React from 'react';

export interface PanelProps {
  title?: string;
  meta?: React.ReactNode;
  actions?: React.ReactNode;
  compact?: boolean;
  children?: React.ReactNode;
  className?: string;
}

export function Panel({ title, meta, actions, compact, children, className = '' }: PanelProps) {
  const hasHeader = title || meta || actions;
  const headerClass = compact ? 'sh-page-panel-header-compact' : 'sh-page-panel-header';

  return (
    <div className={`sh-page-panel ${className}`}>
      {hasHeader && (
        <div className={headerClass}>
          <div>
            {title && <h3 className="sh-page-panel-title">{title}</h3>}
            {meta && <div className="sh-page-panel-meta">{meta}</div>}
          </div>
          {actions && <div className="sh-page-actions">{actions}</div>}
        </div>
      )}
      {children && <div className="sh-page-panel-body">{children}</div>}
    </div>
  );
}
