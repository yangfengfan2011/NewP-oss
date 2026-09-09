import type { CSSProperties, ReactNode } from 'react';
import { Card, Space, Typography, theme } from 'antd';

const { Title, Paragraph } = Typography;

export interface CrudWorkspacePageProps {
  embedded?: boolean;
  title?: ReactNode;
  description?: ReactNode;
  primaryActions?: ReactNode;
  filterBar?: ReactNode;
  children: ReactNode;
}

export function CrudWorkspacePage({
  embedded = false,
  title,
  description,
  primaryActions,
  filterBar,
  children,
}: CrudWorkspacePageProps) {
  const { token } = theme.useToken();
  const hasHeader = Boolean((!embedded && (title || description)) || primaryActions);

  const content = (
    <div className="np-crud-workspace-body">
      {hasHeader ? (
        <div className="np-crud-workspace-header">
          {!embedded && (title || description) && <div className="np-crud-workspace-heading">
            {title ? (
              <Title level={3} className="np-crud-workspace-title">
                {title}
              </Title>
            ) : null}
            {description ? <Paragraph className="np-crud-workspace-description">{description}</Paragraph> : null}
          </div>}
          {primaryActions ? <Space className="np-crud-workspace-actions">{primaryActions}</Space> : null}
        </div>
      ) : null}
      {filterBar ? (
        <div className="np-crud-workspace-filter">
          {filterBar}
        </div>
      ) : null}
      <div className="np-crud-workspace-content">
        {children}
      </div>
    </div>
  );
  return <div className={`np-crud-workspace${embedded ? ' np-crud-embedded' : ''}`} style={{
    '--np-crud-subtle': token.colorFillAlter,
    '--np-crud-radius': `${token.borderRadius}px`,
  } as CSSProperties}>
    {embedded ? content : <Card size="small" className="np-crud-workspace-panel">{content}</Card>}
  </div>;
}
