import type { ReactNode } from 'react';
import { Button, Drawer, Space } from 'antd';
import { AlertState } from './AlertState';
import { LoadingBlock } from './LoadingBlock';

export interface CrudFormDrawerProps {
  open: boolean;
  title: ReactNode;
  loading?: boolean;
  saving?: boolean;
  error?: string | null;
  children: ReactNode;
  submitFormId?: string;
  primaryLabel?: ReactNode;
  primaryAction?: () => void;
  hidePrimary?: boolean;
  onSubmit?: () => void;
  onClose: () => void;
}

export function CrudFormDrawer({
  open,
  title,
  loading = false,
  saving = false,
  error = null,
  children,
  submitFormId,
  primaryLabel = '保存',
  primaryAction,
  hidePrimary = false,
  onSubmit,
  onClose,
}: CrudFormDrawerProps) {
  const primaryButton = hidePrimary ? null : (
    <Button
      type="primary"
      htmlType={submitFormId ? 'submit' : 'button'}
      form={submitFormId}
      onClick={submitFormId ? undefined : (primaryAction ?? onSubmit)}
      loading={saving}
      disabled={loading}
    >
      {primaryLabel}
    </Button>
  );

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
          {primaryButton}
        </Space>
      }
    >
      {loading ? <LoadingBlock message="正在加载详情..." /> : null}
      {error ? <AlertState type="error" message={error} /> : null}
      {!loading ? children : null}
    </Drawer>
  );
}
