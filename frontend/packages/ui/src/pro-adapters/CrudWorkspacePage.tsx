import type { ReactNode } from 'react';
import { Space, Typography } from 'antd';

const { Title, Paragraph } = Typography;

export interface CrudWorkspacePageProps {
  title: ReactNode;
  description?: ReactNode;
  primaryActions?: ReactNode;
  filterBar?: ReactNode;
  children: ReactNode;
}

export function CrudWorkspacePage({
  title,
  description,
  primaryActions,
  filterBar,
  children,
}: CrudWorkspacePageProps) {
  return (
    <div className="np-crud-workspace">
      <div className="np-crud-workspace-header">
        <div>
          <Title level={3} className="np-crud-workspace-title">
            {title}
          </Title>
          {description ? <Paragraph className="np-crud-workspace-description">{description}</Paragraph> : null}
        </div>
        {primaryActions ? <Space className="np-crud-workspace-actions">{primaryActions}</Space> : null}
      </div>
      {filterBar ? <div className="np-crud-workspace-filter">{filterBar}</div> : null}
      <div className="np-crud-workspace-content">{children}</div>
    </div>
  );
}
