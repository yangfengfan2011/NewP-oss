import { lazy, Suspense, type ReactElement, type ReactNode } from 'react';
import type { ProDescriptionsItemProps, ProDescriptionsProps } from '@ant-design/pro-descriptions';
import { LoadingBlock } from './LoadingBlock';

type LazyProDescriptions = {
  <TRecord extends Record<string, unknown>>(props: ProDescriptionsProps<TRecord>): ReactElement;
};

const ProDescriptions = lazy(() => import('@ant-design/pro-descriptions')) as unknown as LazyProDescriptions;

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
    <Suspense fallback={<LoadingBlock message="正在加载详情视图..." />}>
      <ProDescriptions<TRecord>
        title={title}
        dataSource={dataSource}
        columns={columns}
        loading={loading}
        column={column}
        extra={actions}
        bordered
      />
    </Suspense>
  );
}
