import { useLocalization } from '@icp-platform/core';
import type { ReactElement } from 'react';
import {
  type StandardCrudSummaryBlock,
  type StandardCrudSummaryRenderer,
} from '@icp-platform/sdk';
import { SummaryCard } from '@icp-platform/ui/core';

interface StandardCrudSummaryBlocksProps {
  blocks: string[];
  summaries: Record<string, StandardCrudSummaryBlock<unknown> | undefined>;
  renderers?: Record<string, StandardCrudSummaryRenderer>;
  titleForBlock: (key: string) => string;
  featureActions?: import('@icp-platform/sdk').StandardCrudSummaryRendererContext['featureActions'];
  dataAccess?: import('@icp-platform/sdk').ActionDataAccessRequirement;
}

/**
 * 标准摘要运行时只解释平台状态和受控 renderer 分发，不读取元数据或触发请求。
 * 模块 renderer 只会在 ready 态被调用，避免模块承担平台降级状态协议。
 */
export function StandardCrudSummaryBlocks({
  blocks,
  summaries,
  renderers,
  titleForBlock,
  featureActions,
  dataAccess,
}: StandardCrudSummaryBlocksProps): ReactElement | null {
  const { t } = useLocalization();
  if (blocks.length === 0) {
    return null;
  }

  return (
    <div className="sh-section-grid">
      {blocks.map((blockKey) => {
        const block = summaries[blockKey] ?? { status: 'notConfigured', data: null };
        const title = titleForBlock(blockKey);
        const renderer = renderers?.[blockKey];

        if (block.status === 'ready' && renderer) {
          return <div key={blockKey}>{renderer({ blockKey, block, title, ...(featureActions ? { featureActions } : {}), ...(dataAccess ? { dataAccess } : {}) })}</div>;
        }

        return (
          <SummaryCard
            key={blockKey}
            label={title}
            value={block.status === 'notConfigured' ? t('Platform.NotConfigured', '暂未配置') : block.status === 'dependencyUnavailable' ? t('Platform.DependencyUnavailable', '依赖不可用') : t('Platform.Ready', '已就绪')}
          />
        );
      })}
    </div>
  );
}
