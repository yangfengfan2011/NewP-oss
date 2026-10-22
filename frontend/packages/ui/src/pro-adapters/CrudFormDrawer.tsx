import { useLocalization } from '@icp-platform/core';
import type { ReactNode } from 'react';
import { Button, Drawer, Space } from 'antd';
import { AlertState } from './AlertState';
import { LoadingBlock } from './LoadingBlock';

export interface CrudFormDrawerProps {
  open: boolean;
  title: ReactNode;
  headerActions?: ReactNode;
  loading?: boolean;
  saving?: boolean;
  error?: string | null;
  errorActions?: ReactNode;
  children: ReactNode;
  submitFormId?: string;
  primaryLabel?: ReactNode;
  primaryAction?: () => void;
  hidePrimary?: boolean;
  primaryDisabled?: boolean;
  primaryDisabledReason?: ReactNode;
  secondaryAction?: ReactNode;
  onSubmit?: () => void;
  onClose: () => void;
  afterOpenChange?: (open: boolean) => void;
}

export function CrudFormDrawer({
  open,
  title,
  headerActions,
  loading = false,
  saving = false,
  error = null,
  errorActions,
  children,
  submitFormId,
  primaryLabel,
  primaryAction,
  hidePrimary = false,
  primaryDisabled = false,
  primaryDisabledReason,
  secondaryAction,
  onSubmit,
  onClose,
  afterOpenChange,
}: CrudFormDrawerProps) {
  const { t } = useLocalization();
  const primaryButton = hidePrimary ? null : (
    <Space direction="vertical" size={2} align="end">
      <Button
        type="primary"
        htmlType={submitFormId ? 'submit' : 'button'}
        form={submitFormId}
        onClick={submitFormId ? undefined : (primaryAction ?? onSubmit)}
        loading={saving}
        disabled={loading || primaryDisabled}
      >
        {primaryLabel ?? t('Platform.Save', '保存')}
      </Button>
      {primaryDisabled && primaryDisabledReason ? (
        <span role="status" className="np-crud-form-drawer-disabled-reason">
          {primaryDisabledReason}
        </span>
      ) : null}
    </Space>
  );

  return (
    <Drawer
      open={open}
      title={title}
      extra={headerActions}
      width="min(720px, calc(100vw - 16px))"
      onClose={onClose}
      afterOpenChange={afterOpenChange}
      destroyOnHidden
      footer={
        <Space className="np-crud-form-drawer-footer">
          <Button onClick={onClose} disabled={saving}>
            {t('Platform.Cancel', '取消')}
          </Button>
          {secondaryAction}
          {primaryButton}
        </Space>
      }
    >
      {loading ? <LoadingBlock message={t('Platform.LoadingDetail', '正在加载详情...')} /> : null}
      {error ? (
        <div className="np-crud-form-drawer-error">
          <AlertState type="error" message={error} />
          {errorActions}
        </div>
      ) : null}
      <div hidden={loading}>{children}</div>
    </Drawer>
  );
}
