import type { ReactNode } from 'react';
import { Spin } from 'antd';

export interface LoadingBlockProps {
  message?: ReactNode;
}

export function LoadingBlock({ message }: LoadingBlockProps) {
  return (
    <div style={{ display: 'flex', justifyContent: 'center', padding: 24 }}>
      <Spin tip={message}>
        <span style={{ display: 'block', minHeight: 24 }} />
      </Spin>
    </div>
  );
}
