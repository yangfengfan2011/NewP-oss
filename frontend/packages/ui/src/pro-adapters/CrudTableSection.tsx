import { ReloadOutlined } from '@ant-design/icons';
import { Button } from 'antd';
import type { SearchTablePageProps } from './SearchTablePage';
import { SearchTablePage } from './SearchTablePage';

export interface CrudTableSectionProps<TRow extends Record<string, unknown>> extends SearchTablePageProps<TRow> {
  loading?: boolean;
  onRefresh: () => void;
}

export function CrudTableSection<TRow extends Record<string, unknown>>({
  loading = false,
  onRefresh,
  toolbar,
  ...tableProps
}: CrudTableSectionProps<TRow>) {
  return (
    <div className="np-crud-table-section">
      <SearchTablePage<TRow>
        {...tableProps}
        footerFill={<div className="np-crud-table-section-fill" aria-hidden="true" />}
        toolbar={
          <>
            <Button size="small" icon={<ReloadOutlined />} onClick={onRefresh} loading={loading}>
              刷新
            </Button>
            {toolbar}
          </>
        }
      />
    </div>
  );
}
