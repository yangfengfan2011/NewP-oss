import { useLocalization } from '@icp-platform/core';
import { lazy, Suspense, type CSSProperties, type ReactElement, type ReactNode } from 'react';
import theme from 'antd/es/theme';
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
  const { t } = useLocalization();
  const { token } = theme.useToken();
  return (
    <Suspense fallback={<LoadingBlock message={t('Platform.LoadingDetailView', '正在加载详情视图...')} />}>
      <ProDescriptions<TRecord>
        className="np-crud-detail"
        style={{ '--np-crud-secondary': token.colorTextSecondary, '--np-crud-surface': token.colorBgContainer } as CSSProperties}
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
