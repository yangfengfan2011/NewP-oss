import { useState } from 'react';
import { Alert, Button, Modal, Space, Typography } from 'antd';
import type {
  StandardCrudOperationResult,
  StandardCrudOperationResultPresenter,
} from '@icp-platform/sdk';

interface IdentityCredentialIssuancePayload {
  account: Record<string, unknown>;
  temporaryCredential: string;
}

function readIdentityCredentialIssuance(
  result: StandardCrudOperationResult,
): IdentityCredentialIssuancePayload | null {
  if ((result.operation !== 'create' && result.operation !== 'action') || typeof result.payload !== 'object' || result.payload === null) {
    return null;
  }

  const payload = result.payload as Record<string, unknown>;
  if (
    typeof payload.account !== 'object'
    || payload.account === null
    || typeof payload.temporaryCredential !== 'string'
    || payload.temporaryCredential.length === 0
  ) {
    return null;
  }

  return {
    account: payload.account as Record<string, unknown>,
    temporaryCredential: payload.temporaryCredential,
  };
}

function IdentityCredentialResult({
  value,
  close,
}: {
  value: string;
  close: () => void;
}) {
  const [copyStatus, setCopyStatus] = useState<string | null>(null);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopyStatus('已复制。关闭后将无法再次查看。');
    } catch {
      setCopyStatus('复制失败，请现在手动复制。');
    }
  };

  return (
    <Modal
      open
      title="一次性临时密码"
      closable={false}
      maskClosable={false}
      keyboard={false}
      destroyOnHidden
      onCancel={close}
      footer={(
        <Button type="primary" onClick={close}>
          我已安全保存，关闭
        </Button>
      )}
    >
      <Space direction="vertical" size="middle" style={{ width: '100%' }}>
        <Alert
          type="warning"
          showIcon
          message="这是唯一一次显示机会。关闭后页面状态会立即销毁该秘密，无法查询或恢复。"
        />
        <Typography.Text code aria-label="临时密码">{value}</Typography.Text>
        <Button onClick={() => void copy()}>复制临时密码</Button>
        {copyStatus ? <Typography.Text role="status">{copyStatus}</Typography.Text> : null}
      </Space>
    </Modal>
  );
}

// Identity presenter 是唯一读取临时凭据 shape 的前端绑定；共享 Host 只持有不透明结果。
export const identityAccountOperationResultPresenter: StandardCrudOperationResultPresenter = {
  supports: (result) => readIdentityCredentialIssuance(result) !== null,
  render: ({ result, close }) => {
    const issuance = readIdentityCredentialIssuance(result);
    return issuance ? <IdentityCredentialResult value={issuance.temporaryCredential} close={close} /> : null;
  },
};
