import React from 'react';

export interface PageHeaderProps {
  title: string;
  eyebrow?: string;
  description?: string;
  actions?: React.ReactNode;
}

export function PageHeader({ title, eyebrow, description, actions }: PageHeaderProps) {
  return (
    <div className="sh-page-header">
      <div className="sh-page-header-main">
        {eyebrow && <span className="sh-page-eyebrow">{eyebrow}</span>}
        <h1 className="sh-page-title">{title}</h1>
        {description && <p className="sh-page-description">{description}</p>}
      </div>
      {actions && <div className="sh-page-actions">{actions}</div>}
    </div>
  );
}
