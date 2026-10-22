import type { ComponentProps, ReactElement } from 'react';
import { useLocalization } from '@icp-platform/core';
import AntdDrawer from 'antd/es/drawer';
import AntdModal from 'antd/es/modal';

export function StandardCrudListDrawer(props: ComponentProps<typeof AntdDrawer>): ReactElement {
  return <AntdDrawer {...props} />;
}

export function StandardCrudListModal(props: ComponentProps<typeof AntdModal>): ReactElement {
  const { t } = useLocalization();
  const closable = props.closable === false ? false : {
    'aria-label': t('Platform.Close', '关闭'),
    ...(typeof props.closable === 'object' ? props.closable : {}),
  };
  return <AntdModal {...props} closable={closable} />;
}
