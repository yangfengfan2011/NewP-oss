import { Drawer } from '../components/Drawer';
import { Panel } from './Panel';
import type { StandardListPageProps } from './StandardListPage.types';

export function StandardListPage({
  headerActions,
  filterBar,
  content,
  footerLeft,
  footerRight,
  advancedFilterTrigger,
  advancedFilterTitle = '高级筛选',
  advancedFilterOpen = false,
  advancedFilterContent,
  onAdvancedFilterClose,
}: StandardListPageProps) {
  const hasFooter = Boolean(footerLeft || footerRight);

  return (
    <div className="sh-page">
      {headerActions && <div className="sh-page-actions">{headerActions}</div>}

      {filterBar ? (
        <section className="sh-standard-list-page-filter">
          {filterBar}
        </section>
      ) : null}

      <section className="sh-standard-list-page-content">
        {content}
      </section>

      {hasFooter ? (
        <Panel compact className="sh-standard-list-page-footer-panel">
          <div className="sh-standard-list-page-footer">
            <div className="sh-standard-list-page-footer-left">{footerLeft}</div>
            <div className="sh-standard-list-page-footer-right">{footerRight}</div>
          </div>
        </Panel>
      ) : null}

      {advancedFilterTrigger}

      <Drawer
        open={advancedFilterOpen}
        onClose={onAdvancedFilterClose ?? (() => {})}
        title={advancedFilterTitle}
      >
        {advancedFilterContent}
      </Drawer>
    </div>
  );
}

export default StandardListPage;
