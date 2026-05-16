import { Panel, StatusBadge } from '@icp-platform/ui';
import type { StandardCrudSummaryBlock } from '@icp-platform/sdk';
import type { PersonUserLinkSummaryData } from '../services/organizationService';

interface PersonUserLinkCardProps {
  summary: StandardCrudSummaryBlock<PersonUserLinkSummaryData>;
  title?: string;
}

export default function PersonUserLinkCard({ summary, title = '账号绑定' }: PersonUserLinkCardProps) {
  if (summary.status !== 'ready' || !summary.data) {
    return null;
  }

  const data = summary.data;

  return (
    <Panel
      title={title}
      meta="首轮只保留状态化摘要块位与只读降级展示，不恢复复杂绑定交互"
    >
      <div style={{ display: 'grid', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <StatusBadge
            variant="success"
            label="已绑定"
          />
          <span style={{ color: '#64748b', fontSize: 12 }}>
            账号绑定信息可直接读出。
          </span>
        </div>

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
      </div>
    </Panel>
  );
}
