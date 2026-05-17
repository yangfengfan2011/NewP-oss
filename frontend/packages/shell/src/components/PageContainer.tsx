import type { ReactNode } from 'react';
import { Breadcrumb, Typography } from 'antd';
import { Link } from 'react-router-dom';

const { Title, Paragraph } = Typography;

export interface PageContainerProps {
  title?: string;
  subtitle?: string;
  breadcrumbItems?: Array<{ title: string; path?: string }>;
  actions?: ReactNode;
  children: ReactNode;
}

export function PageContainer({
  title,
  subtitle,
  breadcrumbItems = [],
  actions,
  children,
}: PageContainerProps) {
  return (
    <section className="np-page-container">
      {breadcrumbItems.length > 0 ? (
        <Breadcrumb
          className="np-page-breadcrumb"
          items={breadcrumbItems.map((item) => ({
            title: item.path ? <Link to={item.path}>{item.title}</Link> : item.title,
          }))}
        />
      ) : null}
      <div className="np-page-header">
        <div>
          {title ? <Title level={3} className="np-page-title">{title}</Title> : null}
          {subtitle ? <Paragraph className="np-page-subtitle">{subtitle}</Paragraph> : null}
        </div>
        {actions ? <div className="np-page-actions">{actions}</div> : null}
      </div>
      <div className="np-page-content">{children}</div>
    </section>
  );
}
