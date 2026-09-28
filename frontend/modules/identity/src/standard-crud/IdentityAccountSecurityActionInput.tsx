import { useLocalization } from '@icp-platform/core';
import { Alert, Descriptions, Form, Input, Space, Typography } from 'antd';
import type { StandardCrudActionInputRenderer } from '@icp-platform/sdk';

interface IdentitySecurityActionInputValue {
  reason: string;
  confirmation: string;
}

function readRecord(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

function readSnapshot(detail: unknown, row: unknown) {
  const detailRecord = readRecord(detail);
  const fields = readRecord(detailRecord.fields);
  const source = Object.keys(fields).length > 0 ? fields : Object.keys(detailRecord).length > 0 ? detailRecord : readRecord(row);
  return {
    ecode: String(source.ECODE ?? source.ecode ?? ''),
    ename: String(source.ENAME ?? source.ename ?? ''),
    status: String(source.Status ?? source.status ?? ''),
  };
}

function readValue(value: unknown): IdentitySecurityActionInputValue {
  const record = readRecord(value);
  return {
    reason: typeof record.reason === 'string' ? record.reason : '',
    confirmation: typeof record.confirmation === 'string' ? record.confirmation : '',
  };
}

function createIdentitySecurityActionInput(includeDeleteConfirmation: boolean): StandardCrudActionInputRenderer {
  function IdentitySecurityActionInput({ value, onChange, detail, row }: Parameters<StandardCrudActionInputRenderer>[0]) {
    const { t, getEnumerationLabel } = useLocalization();
    const current = readValue(value);
    const snapshot = readSnapshot(detail, row);
    return (
      <Space direction="vertical" size="middle" style={{ width: '100%' }}>
        <Descriptions size="small" column={1} bordered>
          <Descriptions.Item label={t('IdentityMessages.AccountCode', '登录账号')}>{snapshot.ecode}</Descriptions.Item>
          <Descriptions.Item label={t('IdentityMessages.DisplayName', '显示名称')}>{snapshot.ename}</Descriptions.Item>
          <Descriptions.Item label={t('IdentityMessages.CurrentStatus', '当前状态')}>{getEnumerationLabel('IdentityAccountStatus', snapshot.status, snapshot.status)}</Descriptions.Item>
        </Descriptions>
        {includeDeleteConfirmation ? (
          <Alert
            type="error"
            showIcon
            message={t('IdentityMessages.DeleteWarning', '该操作不可恢复。账号将保留墓碑，凭据会被移除，全部会话会被撤销。')}
          />
        ) : null}
        <Form layout="vertical" requiredMark>
          <Form.Item label={t('IdentityMessages.Reason', '操作原因')} required>
            <Input.TextArea
              aria-label={t('IdentityMessages.Reason', '操作原因')}
              value={current.reason}
              maxLength={500}
              showCount
              rows={3}
              onChange={(event) => onChange({ ...current, reason: event.target.value })}
            />
          </Form.Item>
          {includeDeleteConfirmation ? (
            <Form.Item label={t('IdentityMessages.DeleteConfirmation', '完整输入登录账号 {accountCode}', { accountCode: snapshot.ecode })} required>
              <Input
                aria-label={t('IdentityMessages.DeleteConfirmationLabel', '完整登录账号确认')}
                value={current.confirmation}
                autoComplete="off"
                onChange={(event) => onChange({ ...current, confirmation: event.target.value })}
              />
            </Form.Item>
          ) : null}
        </Form>
        <Typography.Text type="secondary">{t('IdentityMessages.SecurityRecheck', '确认后仍由服务端按最新 Revision 与保护规则重新裁决。')}</Typography.Text>
      </Space>
    );
  }
  return context => <IdentitySecurityActionInput {...context} />;
}

// Identity 独占确认文案和业务输入；共享 Host 只持有不透明值并管理弹层生命周期。
export const identityAccountSecurityReasonInput = createIdentitySecurityActionInput(false);
export const identityAccountDeleteConfirmationInput = createIdentitySecurityActionInput(true);
