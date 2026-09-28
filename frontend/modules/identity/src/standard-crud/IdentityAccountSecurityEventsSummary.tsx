import { useState } from 'react';
import { formatRequestError, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { StandardCrudRequestError, type StandardCrudSummaryRenderer } from '@icp-platform/sdk';
import { Alert, Button, Card, Empty, Pagination, Space, Spin, Table } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import {
  listAccountSecurityEvents,
  type IdentityPage,
  type IdentitySecurityEvent,
} from '../api/identityClient';

const EVENT_PAGE_SIZE = 20;

interface IdentityAccountSecurityEventsSummaryProps {
  accountEid: string;
  featurePosition?: string;
  title: string;
}


export function IdentityAccountSecurityEventsSummary({
  accountEid,
  featurePosition,
  title,
}: IdentityAccountSecurityEventsSummaryProps) {
  const { t } = useLocalization();
  const eventColumns: ColumnsType<IdentitySecurityEvent> = [
    { key: 'occurredAt', title: t('IdentityMessages.EventTime', '时间'), dataIndex: 'occurredAt_DisplayValue' },
    { key: 'eventType', title: t('IdentityMessages.EventType', '类型'), dataIndex: 'eventType' },
    { key: 'reason', title: t('IdentityMessages.EventReason', '原因'), dataIndex: 'reason' },
    { key: 'actorKind', title: t('IdentityMessages.EventActorKind', '操作者类型'), dataIndex: 'actorKind' },
    { key: 'traceId', title: 'TraceId', dataIndex: 'traceId' },
  ];
  const [page, setPage] = useState(1);
  // 查询身份只包含影响安全事件事实的 Identity 输入；标题与安全命令状态不参与复用。
  const query = usePlatformQuery<IdentityPage<IdentitySecurityEvent>>({
    key: ['identity.security-events', accountEid, page, EVENT_PAGE_SIZE, featurePosition ?? ''],
    load: ({ signal }) => listAccountSecurityEvents(accountEid, page, EVENT_PAGE_SIZE, { signal, ...(featurePosition ? { featurePosition } : {}) }),
  });
  const result = query.data;
  const loading = result === undefined && (query.status === 'idle' || query.isFetching);
  const refreshing = result !== undefined && query.isFetching;
  const error = query.status === 'error';

  return (
    <Card
      title={title}
      extra={result ? (
        <Space size="small">
          <span>{t('IdentityMessages.EventCount', '共 {count} 条', { count: result.totalCount })}</span>
          <Button
            type="link"
            size="small"
            aria-label={t('IdentityMessages.RefreshEvents', '刷新安全事件')}
            loading={refreshing}
            onClick={query.refresh}
          >
            {t('IdentityMessages.Refresh', '刷新')}
          </Button>
        </Space>
      ) : undefined}
    >
      {loading ? <Spin aria-label={t('IdentityMessages.EventsLoading', '安全事件加载中')} /> : null}
      {!loading && error ? (
        <Alert
          type="error"
          showIcon
          message={query.error instanceof StandardCrudRequestError ? formatRequestError(query.error, t) : t('IdentityMessages.EventsFailed', '安全事件加载失败，请稍后重试。')}
          action={<Button size="small" aria-label={t('IdentityMessages.RetryEvents', '重试安全事件')} onClick={query.retry}>{t('IdentityMessages.Retry', '重试')}</Button>}
        />
      ) : null}
      {!loading && !error && result?.items.length === 0 ? <Empty description={t('IdentityMessages.EventsEmpty', '暂无安全事件。')} /> : null}
      {!loading && !error && result && result.items.length > 0 ? (
        <Table<IdentitySecurityEvent>
          rowKey="eid"
          size="small"
          columns={eventColumns}
          dataSource={result.items}
          pagination={false}
          scroll={{ x: 'max-content' }}
        />
      ) : null}
      {!loading && !error && result && result.totalCount > result.pageSize ? (
        <Pagination
          current={result.page}
          pageSize={result.pageSize}
          total={result.totalCount}
          showSizeChanger={false}
          onChange={setPage}
        />
      ) : null}
    </Card>
  );
}

export const identityAccountSecurityEventsSummaryRenderer: StandardCrudSummaryRenderer = ({
  block,
  title,
}) => {
  const accountEid = typeof block.data === 'object'
    && block.data !== null
    && 'accountEid' in block.data
    && typeof block.data.accountEid === 'string'
    ? block.data.accountEid
    : null;
  const featurePosition = typeof block.data === 'object' && block.data !== null
    && 'featurePosition' in block.data && typeof block.data.featurePosition === 'string' ? block.data.featurePosition : undefined;
  return accountEid
    ? <IdentityAccountSecurityEventsSummary key={accountEid} accountEid={accountEid} title={title} featurePosition={featurePosition} />
    : <IdentityEventsUnavailable />;
};

function IdentityEventsUnavailable() {
  const { t } = useLocalization();
  return <Alert type="error" showIcon message={t('IdentityMessages.EventsTargetUnavailable', '安全事件目标账号不可用。')} />;
}
