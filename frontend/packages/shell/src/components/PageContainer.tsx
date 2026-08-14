import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

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
  const shouldRenderHeader = Boolean(title || subtitle || actions);

  return (
    <section className="np-page-container">
      {breadcrumbItems.length > 0 ? (
        <nav className="np-page-breadcrumb" aria-label="面包屑">
          <ol className="np-page-breadcrumb-list">
            {breadcrumbItems.map((item, index) => (
              <li key={`${item.path ?? item.title}-${index}`}>
                {item.path ? <Link to={item.path}>{item.title}</Link> : <span>{item.title}</span>}
              </li>
            ))}
          </ol>
        </nav>
      ) : null}
      {shouldRenderHeader ? (
        <div className="np-page-header">
          <div>
            {title ? <h3 className="np-page-title">{title}</h3> : null}
            {subtitle ? <p className="np-page-subtitle">{subtitle}</p> : null}
          </div>
          {actions ? <div className="np-page-actions">{actions}</div> : null}
        </div>
      ) : null}
      <div className="np-page-content">{children}</div>
    </section>
  );
}
