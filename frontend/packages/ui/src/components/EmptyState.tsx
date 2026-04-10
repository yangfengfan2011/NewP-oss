import React from 'react';

export interface EmptyStateProps {
  message?: string;
  children?: React.ReactNode;
}

export function EmptyState({ message = '暂无数据', children }: EmptyStateProps) {
  return (
    <div className="sh-page-empty">
      {children || message}
    </div>
  );
}
