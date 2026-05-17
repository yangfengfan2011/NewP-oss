import type { ReactNode } from 'react';
import { Button, Drawer, Space } from 'antd';
import { AlertState } from './AlertState';

export interface CrudFormDrawerProps {
  open: boolean;
  title: ReactNode;
  loading?: boolean;
  saving?: boolean;
  error?: string | null;
  children: ReactNode;
  onSubmit: () => void;
  onClose: () => void;
}

export function CrudFormDrawer({
  open,
  title,
  loading = false,
  saving = false,
  error = null,
  children,
  onSubmit,
  onClose,
}: CrudFormDrawerProps) {
  return (
    <Drawer
      open={open}
      title={title}
      width={720}
      onClose={onClose}
      destroyOnClose
      footer={
        <Space className="np-crud-form-drawer-footer">
          <Button onClick={onClose} disabled={saving}>
            取消
          </Button>
          <Button type="primary" onClick={onSubmit} loading={saving} disabled={loading}>
            保存
          </Button>
        </Space>
      }
    >
      {error ? <AlertState type="error" message={error} /> : null}
      {children}
    </Drawer>
  );
}
