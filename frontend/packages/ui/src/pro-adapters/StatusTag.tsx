import type { ReactNode } from 'react';
import { Tag } from 'antd';

export type StatusTagTone = 'default' | 'success' | 'processing' | 'warning' | 'error';

export interface StatusTagProps {
  tone?: StatusTagTone;
  children: ReactNode;
}

const statusColors: Record<StatusTagTone, string | undefined> = {
  default: undefined,
  success: 'success',
  processing: 'processing',
  warning: 'warning',
  error: 'error',
};

export function StatusTag({ tone = 'default', children }: StatusTagProps) {
  return <Tag color={statusColors[tone]}>{children}</Tag>;
}
