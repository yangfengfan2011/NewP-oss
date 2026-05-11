import { EmptyState, Panel, StatusBadge } from '@icp-platform/ui';
import type { StandardCrudSummaryBlock } from '../services/organizationService';

interface AuthorizationSummaryCardProps {
  summary: StandardCrudSummaryBlock<string[]>;
  title?: string;
}

function getSummaryStateLabel(status: StandardCrudSummaryBlock['status']): string {
  if (status === 'ready') {
    return '已加载';
  }

  if (status === 'dependencyUnavailable') {
    return '依赖不可用';
  }

  return '未配置';
}

export default function AuthorizationSummaryCard({ summary, title = '授权摘要' }: AuthorizationSummaryCardProps) {
  const items = summary.status === 'ready' && Array.isArray(summary.data) ? summary.data : [];
  const hasItems = items.length > 0;

  return (
    <Panel
      title={title}
      meta="首轮只保留状态化块位和只读降级，不把授权编辑链路拉回人员中心"
    >
      <div style={{ display: 'grid', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <StatusBadge
            variant={summary.status === 'ready' ? 'success' : summary.status === 'dependencyUnavailable' ? 'warning' : 'neutral'}
            label={getSummaryStateLabel(summary.status)}
          />
          <span style={{ color: '#64748b', fontSize: 12 }}>
            {summary.status === 'ready'
              ? '授权摘要可以只读展示。'
              : summary.status === 'dependencyUnavailable'
                ? '授权摘要依赖暂不可用，当前页面只保留降级结果。'
                : '授权摘要块位已保留，但当前尚未形成可读数据。'}
          </span>
        </div>

        {hasItems ? (
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
        ) : (
          <EmptyState
            message={
              summary.status === 'dependencyUnavailable'
                ? '授权摘要依赖暂不可用，当前仅保留只读降级块位。'
                : '暂无授权摘要，或摘要依赖尚未就绪。'
            }
          />
        )}
      </div>
    </Panel>
  );
}
