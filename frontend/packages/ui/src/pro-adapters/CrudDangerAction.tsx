import type { ReactNode } from 'react';
import { Button, Popconfirm } from 'antd';

export interface CrudDangerActionProps {
  children: ReactNode;
  confirmMessage: string;
  loading?: boolean;
  disabled?: boolean;
  onConfirm: () => void | Promise<void>;
}

export function CrudDangerAction({
  children,
  confirmMessage,
  loading = false,
  disabled = false,
  onConfirm,
}: CrudDangerActionProps) {
  return (
    <Popconfirm
      title={confirmMessage}
      okText="确认"
      cancelText="取消"
      okButtonProps={{ danger: true, loading }}
      onConfirm={onConfirm}
    >
      <Button type="link" danger loading={loading} disabled={disabled}>
        {children}
      </Button>
    </Popconfirm>
  );
}
