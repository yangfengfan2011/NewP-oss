import { useLocalization } from '@icp-platform/core';
import type { StandardCrudOperationResultPresenterContext } from '@icp-platform/sdk';
import Button from 'antd/es/button';
import Modal from 'antd/es/modal';
import type { ObjectCopyReceipt, ObjectCopyValuesRow } from './workspaceClipboard';

/** 只展示已确认状态和用户本来已输入的值；恢复时按原确认顺序对应，不读取隐藏的新对象。 */
export default function WorkspaceObjectCopyResult({ result, close }: StandardCrudOperationResultPresenterContext) {
  const { t } = useLocalization();
  const payload = result.payload as { receipt: ObjectCopyReceipt; rows?: ObjectCopyValuesRow[] };
  return <Modal open title={t('Workspace.CopyResultTitle')} onCancel={close} destroyOnHidden
    footer={<Button type="primary" onClick={close}>{t('Workspace.CopyResultClose')}</Button>}>
    <p>{t('Workspace.CopyResultOrder')}</p>
    <ol style={{ paddingInlineStart: 24 }}>
      {payload.receipt.items?.map(item => {
        const row = payload.rows?.find(row => row.sourceLinkId === item.sourceLinkId);
        return <li key={item.sourceLinkId} style={{ marginBlock: 12, overflowWrap: 'anywhere' }}>
          {row && <div>{String(row.values.ECODE ?? '')} · {String(row.values.ENAME ?? '')}</div>}
          <span role="status">{t(item.succeeded ? 'Workspace.ObjectCopied' : item.state === 'cancelled' ? 'Workspace.CopyCancelled' : 'Workspace.CopyNotCommitted')}</span>
        </li>;
      })}
    </ol>
  </Modal>;
}