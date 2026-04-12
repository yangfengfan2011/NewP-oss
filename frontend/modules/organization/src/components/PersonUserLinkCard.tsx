import { Button, EmptyState, Panel, StatusBadge } from '@icp-platform/ui';
import type { PersonUserLinkDto } from '../services/organizationService';

interface PersonUserLinkCardProps {
  link?: PersonUserLinkDto | null;
  onBind?: () => void;
}

export default function PersonUserLinkCard({ link, onBind }: PersonUserLinkCardProps) {
  return (
    <Panel
      title="账号绑定"
      meta="组织模块只展示绑定状态，不在这里创建账号"
      actions={<Button onClick={onBind}>{link ? '重新绑定账号' : '绑定账号'}</Button>}
    >
      {link ? (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
            gap: 16,
          }}
        >
          <div>
            <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>账号</div>
            <div style={{ marginTop: 8, fontSize: 18, fontWeight: 700 }}>{link.userId}</div>
          </div>
          <div>
            <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>绑定状态</div>
            <div style={{ marginTop: 8 }}>
              <StatusBadge variant="success" label="已绑定账号" />
            </div>
          </div>
          <div>
            <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>绑定人</div>
            <div style={{ marginTop: 8, fontWeight: 600 }}>{link.linkedBy}</div>
          </div>
          <div>
            <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>绑定时间</div>
            <div style={{ marginTop: 8, fontWeight: 600 }}>{new Date(link.linkedAt).toLocaleString()}</div>
          </div>
        </div>
      ) : (
        <EmptyState message="当前人员尚未绑定账号。" />
      )}
    </Panel>
  );
}
