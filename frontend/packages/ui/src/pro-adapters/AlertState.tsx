import type { ReactNode } from 'react';
import { Alert } from 'antd';

export interface AlertStateProps {
  message: ReactNode;
  type?: 'success' | 'info' | 'warning' | 'error';
}

export function AlertState({ message, type = 'error' }: AlertStateProps) {
  return <Alert type={type} showIcon message={message} style={{ marginBottom: 16 }} />;
}
