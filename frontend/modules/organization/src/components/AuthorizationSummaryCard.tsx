import { useLocalization } from '@icp-platform/core';
import { Panel, StatusBadge } from '@icp-platform/ui/core';
import type { StandardCrudSummaryBlock } from '@icp-platform/sdk';

interface AuthorizationSummaryCardProps {
  summary: StandardCrudSummaryBlock<string[]>;
  title?: string;
}

export default function AuthorizationSummaryCard({ summary, title }: AuthorizationSummaryCardProps) {
  const { t } = useLocalization();
  if (summary.status !== 'ready' || !summary.data) {
    return null;
  }

  const items = Array.isArray(summary.data) ? summary.data : [];

  return (
    <Panel
      title={title ?? t('Organization.Person.AuthorizationSummaryTitle', '授权摘要')}
      meta={t('Organization.Person.AuthorizationSummaryHelp')}
    >
      <div style={{ display: 'grid', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <StatusBadge
            variant="success"
            label={t('Organization.Person.AuthorizationLoaded', '已加载')}
          />
          <span style={{ color: '#64748b', fontSize: 12 }}>
            {t('Organization.Person.AuthorizationReadOnly', '授权摘要仅供查看。')}
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
