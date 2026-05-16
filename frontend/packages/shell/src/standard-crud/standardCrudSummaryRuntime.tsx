import type { ReactElement } from 'react';
import {
  resolveStandardCrudSummaryMessage,
  type StandardCrudSummaryBlock,
  type StandardCrudSummaryRenderer,
} from '@icp-platform/sdk';
import { SummaryCard } from '@icp-platform/ui';

interface StandardCrudSummaryBlocksProps {
  blocks: string[];
  summaries: Record<string, StandardCrudSummaryBlock<unknown> | undefined>;
  renderers?: Record<string, StandardCrudSummaryRenderer>;
  titleForBlock: (key: string) => string;
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
}: StandardCrudSummaryBlocksProps): ReactElement | null {
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
          return <div key={blockKey}>{renderer({ blockKey, block, title })}</div>;
        }

        return (
          <SummaryCard
            key={blockKey}
            label={title}
            value={resolveStandardCrudSummaryMessage(block) || '已就绪'}
          />
        );
      })}
    </div>
  );
}
