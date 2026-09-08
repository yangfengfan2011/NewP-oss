import type { ReactNode } from 'react';
import { Card, Space, Typography } from 'antd';

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
  if (embedded) return <div className="np-crud-embedded">
    {primaryActions && <Space className="np-crud-workspace-actions">{primaryActions}</Space>}
    {filterBar}
    {children}
  </div>;
  const hasHeader = Boolean(title || description || primaryActions);

  return (
    <div className="np-crud-workspace">
      {hasHeader ? (
        <div className="np-crud-workspace-header">
          <div className="np-crud-workspace-heading">
            {title ? (
              <Title level={3} className="np-crud-workspace-title">
                {title}
              </Title>
            ) : null}
            {description ? <Paragraph className="np-crud-workspace-description">{description}</Paragraph> : null}
          </div>
          {primaryActions ? <Space className="np-crud-workspace-actions">{primaryActions}</Space> : null}
        </div>
      ) : null}
      {filterBar ? (
        <Card size="small" className="np-crud-workspace-filter">
          {filterBar}
        </Card>
      ) : null}
      <Card size="small" className="np-crud-workspace-content">
        {children}
      </Card>
    </div>
  );
}
