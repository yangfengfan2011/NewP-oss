import { useEffect, useState } from 'react';
import type { StandardCrudSummaryRenderer } from '@icp-platform/sdk';
import { Alert, Card, Empty, Pagination, Spin, Table } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import {
  listAccountSecurityEvents,
  type IdentityPage,
  type IdentitySecurityEvent,
} from '../api/identityClient';

const EVENT_PAGE_SIZE = 20;

interface IdentityAccountSecurityEventsSummaryProps {
  accountEid: string;
  title: string;
}

const eventColumns: ColumnsType<IdentitySecurityEvent> = [
  { key: 'occurredAt', title: '时间', dataIndex: 'occurredAt' },
  { key: 'eventType', title: '类型', dataIndex: 'eventType' },
  { key: 'reason', title: '原因', dataIndex: 'reason' },
  { key: 'actorKind', title: '操作者类型', dataIndex: 'actorKind' },
  { key: 'traceId', title: 'TraceId', dataIndex: 'traceId' },
];

export function IdentityAccountSecurityEventsSummary({
  accountEid,
  title,
}: IdentityAccountSecurityEventsSummaryProps) {
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<IdentityPage<IdentitySecurityEvent> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadPage() {
      try {
        setLoading(true);
        setError(null);
        const next = await listAccountSecurityEvents(accountEid, page, EVENT_PAGE_SIZE);
        if (!cancelled) setResult(next);
      } catch {
        if (!cancelled) {
          setResult(null);
          setError('安全事件加载失败，请稍后重试。');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadPage();
    return () => {
      cancelled = true;
    };
  }, [accountEid, page]);

  return (
    <Card title={title} extra={result ? `共 ${result.totalCount} 条` : undefined}>
      {loading ? <Spin aria-label="安全事件加载中" /> : null}
      {!loading && error ? <Alert type="error" showIcon message={error} /> : null}
      {!loading && !error && result?.items.length === 0 ? <Empty description="暂无安全事件。" /> : null}
      {!loading && !error && result && result.items.length > 0 ? (
        <Table<IdentitySecurityEvent>
          rowKey="eid"
          size="small"
          columns={eventColumns}
          dataSource={result.items}
          pagination={false}
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
  return accountEid
    ? <IdentityAccountSecurityEventsSummary key={accountEid} accountEid={accountEid} title={title} />
    : <Alert type="error" showIcon message="安全事件目标账号不可用。" />;
};
