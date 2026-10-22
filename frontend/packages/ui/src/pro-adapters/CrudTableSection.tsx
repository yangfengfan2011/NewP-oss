import { ReloadOutlined } from '@ant-design/icons';
import { useLocalization } from '@icp-platform/core';
import { Button, Space } from 'antd';
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
  const { t } = useLocalization();
  return (
    <div className="np-crud-table-section">
      <SearchTablePage<TRow>
        {...tableProps}
        toolbar={
          <Space>
            <Button size="small" icon={<ReloadOutlined />} onClick={onRefresh} loading={loading} aria-label={t('Platform.Refresh', '刷新')}>
              {t('Platform.Refresh', '刷新')}
            </Button>
            {toolbar}
          </Space>
        }
      />
    </div>
  );
}
