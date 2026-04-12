import { EmptyState, Panel, StatusBadge } from '@icp-platform/ui';

interface AuthorizationSummaryCardProps {
  items: string[];
}

export default function AuthorizationSummaryCard({ items }: AuthorizationSummaryCardProps) {
  return (
    <Panel title="授权摘要" meta="只读展示账号当前角色摘要，写操作仍留在 Authorization 模块">
      {items.length === 0 ? (
        <EmptyState message="暂无授权摘要，或摘要依赖暂不可用。" />
      ) : (
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
      )}
    </Panel>
  );
}
