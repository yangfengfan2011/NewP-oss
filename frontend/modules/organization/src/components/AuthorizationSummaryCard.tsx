import { Panel, StatusBadge } from '@icp-platform/ui';
import type { StandardCrudSummaryBlock } from '@icp-platform/sdk';

interface AuthorizationSummaryCardProps {
  summary: StandardCrudSummaryBlock<string[]>;
  title?: string;
}

export default function AuthorizationSummaryCard({ summary, title = '授权摘要' }: AuthorizationSummaryCardProps) {
  if (summary.status !== 'ready' || !summary.data) {
    return null;
  }

  const items = Array.isArray(summary.data) ? summary.data : [];

  return (
    <Panel
      title={title}
      meta="首轮只保留状态化块位和只读降级，不把授权编辑链路拉回人员中心"
    >
      <div style={{ display: 'grid', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <StatusBadge
            variant="success"
            label="已加载"
          />
          <span style={{ color: '#64748b', fontSize: 12 }}>
            授权摘要可以只读展示。
          </span>
        </div>

        {items.length > 0 ? (
          <div
            style={{
              display: 'flex',
              gap: 10,
              flexWrap: 'wrap',
            }}
          >
            {items.map((item) => (
              <StatusBadge key={item} variant="info" label={item} />
            ))}
          </div>
        ) : null}
      </div>
    </Panel>
  );
}
