import { EmptyState, Panel, StatusBadge } from '@icp-platform/ui';
import type { PersonSummaryBlockDto, PersonUserLinkSummaryData } from '../services/organizationService';

interface PersonUserLinkCardProps {
  summary: PersonSummaryBlockDto<PersonUserLinkSummaryData>;
  title?: string;
}

function getSummaryStateLabel(status: PersonSummaryBlockDto['status']): string {
  if (status === 'ready') {
    return '已绑定';
  }

  if (status === 'dependencyUnavailable') {
    return '依赖不可用';
  }

  return '未配置';
}

export default function PersonUserLinkCard({ summary, title = '账号绑定' }: PersonUserLinkCardProps) {
  const data = summary.status === 'ready' ? summary.data : null;
  const statusLabel = getSummaryStateLabel(summary.status);

  return (
    <Panel
      title={title}
      meta="首轮只保留状态化摘要块位与只读降级展示，不恢复复杂绑定交互"
    >
      <div style={{ display: 'grid', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <StatusBadge
            variant={summary.status === 'ready' ? 'success' : summary.status === 'dependencyUnavailable' ? 'warning' : 'neutral'}
            label={statusLabel}
          />
          <span style={{ color: '#64748b', fontSize: 12 }}>
            {summary.status === 'ready'
              ? '账号绑定信息可直接读出。'
              : summary.status === 'dependencyUnavailable'
                ? '账号绑定依赖暂不可用，当前页面只做降级展示。'
                : '账号绑定块位已保留，但当前尚未形成可读数据。'}
          </span>
        </div>

        {data ? (
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
              gap: 16,
            }}
          >
            <div>
              <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>账号</div>
              <div style={{ marginTop: 8, fontSize: 18, fontWeight: 700 }}>{data.userId}</div>
            </div>
            <div>
              <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>绑定状态</div>
              <div style={{ marginTop: 8 }}>
                <StatusBadge variant="success" label="已绑定账号" />
              </div>
            </div>
            <div>
              <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>绑定人</div>
              <div style={{ marginTop: 8, fontWeight: 600 }}>{data.linkedBy}</div>
            </div>
            <div>
              <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>绑定时间</div>
              <div style={{ marginTop: 8, fontWeight: 600 }}>{new Date(data.linkedAt).toLocaleString()}</div>
            </div>
          </div>
        ) : (
          <EmptyState
            message={
              summary.status === 'dependencyUnavailable'
                ? '账号绑定依赖暂不可用，当前仅保留只读降级块位。'
                : '当前人员尚未形成账号绑定摘要。'
            }
          />
        )}
      </div>
    </Panel>
  );
}
