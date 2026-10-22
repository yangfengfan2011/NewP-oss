import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { DownOutlined } from '@ant-design/icons';
import Button from 'antd/es/button';
import Dropdown from 'antd/es/dropdown';
import type { MenuProps } from 'antd';
import { useLocalization } from '@icp-platform/core';

export interface OverflowActionToolbarItem {
  key: string;
  button: ReactNode;
  menuItem: NonNullable<MenuProps['items']>[number];
}

/** 只负责按实际容器宽度折叠入口；权限、分组及执行仍由调用方持有。 */
export function OverflowActionToolbar({ items, disabled, onAction }: {
  items: OverflowActionToolbarItem[];
  disabled?: boolean;
  onAction: (key: string) => void;
}) {
  const { t } = useLocalization();
  const containerRef = useRef<HTMLDivElement>(null);
  const [visibleCount, setVisibleCount] = useState(items.length);
  const moreLabel = t('Platform.More', '更多');

  useLayoutEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const measure = () => {
      const available = container.getBoundingClientRect().width;
      if (available <= 0) return;
      const nodes = [...container.querySelectorAll<HTMLElement>('.np-action-toolbar-item')];
      const widths = nodes.map(node => node.getBoundingClientRect().width);
      const gap = parseFloat(getComputedStyle(container).columnGap) || 0;
      const total = widths.reduce((sum, width) => sum + width, 0) + Math.max(0, widths.length - 1) * gap;
      let count = widths.length;
      if (total > available) {
        let used = container.querySelector<HTMLElement>('.np-action-toolbar-more')!.getBoundingClientRect().width;
        count = 0;
        for (const width of widths) {
          if (used + gap + width > available) break;
          used += gap + width;
          count += 1;
        }
      }
      setVisibleCount(previous => previous === count ? previous : count);
    };
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(measure);
    observer.observe(container);
    // 收起的按钮仍在原位隐藏测量，语言、字体和动态文案变化也能重新分配空间。
    container.querySelectorAll<HTMLElement>('.np-action-toolbar-item, .np-action-toolbar-more').forEach(node => observer.observe(node));
    return () => observer.disconnect();
  }, [items, moreLabel, visibleCount]);

  const overflow = items.slice(visibleCount);
  return <div ref={containerRef} className="np-action-toolbar" role="toolbar" aria-label={t('Platform.Actions', '操作')}>
    {/* 入口跨越收纳边界时重建弹层，避免窗口调整后留下失去触发按钮的菜单。 */}
    {items.map((item, index) => <span key={`${item.key}:${index >= visibleCount}`} className="np-action-toolbar-item"
      data-overflowed={index >= visibleCount ? 'true' : undefined} aria-hidden={index >= visibleCount ? true : undefined}>
      {item.button}
    </span>)}
    <span key={overflow.map(item => item.key).join(':')} className="np-action-toolbar-more" data-overflowed={overflow.length === 0 ? 'true' : undefined}
      aria-hidden={overflow.length === 0 ? true : undefined}>
      <Dropdown overlayClassName="np-crud-header-menu" trigger={['click']} disabled={disabled || overflow.length === 0} menu={{
        items: overflow.map(item => item.menuItem),
        triggerSubMenuAction: 'click',
        onClick: ({ key }) => onAction(key),
      }}>
        <Button disabled={disabled || overflow.length === 0}>{moreLabel} <DownOutlined aria-hidden /></Button>
      </Dropdown>
    </span>
  </div>;
}
