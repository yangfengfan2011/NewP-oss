import type { ComponentProps, ReactElement } from 'react';
import AntdDrawer from 'antd/es/drawer';
import AntdModal from 'antd/es/modal';

export function StandardCrudListDrawer(props: ComponentProps<typeof AntdDrawer>): ReactElement {
  return <AntdDrawer {...props} />;
}

export function StandardCrudListModal(props: ComponentProps<typeof AntdModal>): ReactElement {
  return <AntdModal {...props} />;
}
