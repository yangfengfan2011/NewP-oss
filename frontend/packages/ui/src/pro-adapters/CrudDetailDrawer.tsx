import type { ReactNode } from 'react';
import { Drawer, Space } from 'antd';
import type { ProDescriptionsItemProps } from '@ant-design/pro-components';
import { AlertState } from './AlertState';
import { DetailPage } from './DetailPage';
import { LoadingBlock } from './LoadingBlock';

export interface CrudDetailDrawerProps<TRecord extends Record<string, unknown>> {
  open: boolean;
  title: ReactNode;
  dataSource?: TRecord;
  columns: ProDescriptionsItemProps<TRecord>[];
  loading?: boolean;
  error?: string | null;
  actions?: ReactNode;
  summary?: ReactNode;
  onClose: () => void;
}

export function CrudDetailDrawer<TRecord extends Record<string, unknown>>({
  open,
  title,
  dataSource,
  columns,
  loading = false,
  error = null,
  actions,
  summary,
  onClose,
}: CrudDetailDrawerProps<TRecord>) {
  return (
    <Drawer
      open={open}
      title={title}
      width={720}
      onClose={onClose}
      extra={actions ? <Space>{actions}</Space> : null}
      destroyOnClose
    >
      {loading ? <LoadingBlock message="正在加载详情..." /> : null}
      {!loading && error ? <AlertState type="error" message={error} /> : null}
      {!loading && !error && dataSource ? (
        <div className="np-crud-detail-drawer-body">
          {summary}
          <DetailPage title="详情信息" dataSource={dataSource} columns={columns} />
        </div>
      ) : null}
    </Drawer>
  );
}
