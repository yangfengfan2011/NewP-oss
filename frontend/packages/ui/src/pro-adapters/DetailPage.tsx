import type { ReactNode } from 'react';
import type { ProDescriptionsItemProps } from '@ant-design/pro-components';
import { ProDescriptions } from '@ant-design/pro-components';

export interface DetailPageProps<TRecord extends Record<string, unknown> = Record<string, unknown>> {
  title?: ReactNode;
  dataSource?: TRecord;
  columns: ProDescriptionsItemProps<TRecord>[];
  loading?: boolean;
  actions?: ReactNode;
  column?: number;
}

export function DetailPage<TRecord extends Record<string, unknown> = Record<string, unknown>>({
  title,
  dataSource,
  columns,
  loading,
  actions,
  column = 2,
}: DetailPageProps<TRecord>) {
  return (
    <ProDescriptions<TRecord>
      title={title}
      dataSource={dataSource}
      columns={columns}
      loading={loading}
      column={column}
      extra={actions}
      bordered
    />
  );
}
