import type { ReactNode } from 'react';
import { ReloadOutlined } from '@ant-design/icons';
import { Button } from 'antd';
import type { SearchTablePageProps } from './SearchTablePage';
import { SearchTablePage } from './SearchTablePage';

export interface CrudTableSectionProps<TRow extends Record<string, unknown>> extends SearchTablePageProps<TRow> {
  title: ReactNode;
  total: number;
  loading?: boolean;
  onRefresh: () => void;
}

export function CrudTableSection<TRow extends Record<string, unknown>>({
  title,
  total,
  loading = false,
  onRefresh,
  toolbar,
  ...tableProps
}: CrudTableSectionProps<TRow>) {
  return (
    <div className="np-crud-table-section">
      <SearchTablePage<TRow>
        {...tableProps}
        headerTitle={
          <div className="np-list-card-title-wrap">
            <span className="np-list-card-title">{title}</span>
            <span className="np-list-card-count">{`共 ${total} 条`}</span>
          </div>
        }
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
