import { Checkbox } from 'antd';
import { useLocalization } from '@icp-platform/core';

/** 只呈现本次新Folder的初始化选择；空间根不是普通父直接授权来源。 */
export default function WorkspaceParentAuthorizationOption({ root, checked, disabled, onChange }: {
  root: boolean; checked: boolean; disabled?: boolean; onChange: (checked: boolean) => void;
}) {
  const { t } = useLocalization();
  return <div>
    <Checkbox checked={!root && checked} disabled={root || disabled} onChange={event => onChange(event.target.checked)}>
      {t('Workspace.CopyParentAuthorization')}
    </Checkbox>
    <p style={{ margin: '8px 0 0' }}>{t(root ? 'Workspace.ParentAuthorizationRoot' : 'Workspace.ParentAuthorizationInitialization')}</p>
  </div>;
}
