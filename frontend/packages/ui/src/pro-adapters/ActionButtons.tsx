import type { ReactNode } from 'react';
import { Button, Dropdown, Popconfirm, Space } from 'antd';
import type { MenuProps } from 'antd';

export interface ActionButtonItem {
  key: string;
  label: ReactNode;
  icon?: ReactNode;
  type?: 'default' | 'primary';
  danger?: boolean;
  disabled?: boolean;
  loading?: boolean;
  confirmMessage?: ReactNode;
  onClick?: () => void | Promise<void>;
}

export interface ActionButtonsProps {
  actions: ActionButtonItem[];
  moreActions?: ActionButtonItem[];
}

export function ActionButtons({ actions, moreActions = [] }: ActionButtonsProps) {
  const menuItems: MenuProps['items'] = moreActions.map((action) => ({
    key: action.key,
    label: action.confirmMessage ? (
      <Popconfirm title={action.confirmMessage} onConfirm={action.onClick}>
        <span>{action.label}</span>
      </Popconfirm>
    ) : action.label,
    disabled: action.disabled,
    danger: action.danger,
    icon: action.icon,
    onClick: action.confirmMessage ? undefined : action.onClick,
  }));

  return (
    <Space size={8} wrap>
      {actions.map((action) => (
        <ActionButton key={action.key} action={action} />
      ))}
      {menuItems.length > 0 ? (
        <Dropdown menu={{ items: menuItems }} trigger={['click']}>
          <Button>更多</Button>
        </Dropdown>
      ) : null}
    </Space>
  );
}

function ActionButton({ action }: { action: ActionButtonItem }) {
  const button = (
    <Button
      type={action.type}
      danger={action.danger}
      disabled={action.disabled}
      loading={action.loading}
      icon={action.icon}
      onClick={action.confirmMessage ? undefined : action.onClick}
    >
      {action.label}
    </Button>
  );

  return action.confirmMessage ? (
    <Popconfirm title={action.confirmMessage} onConfirm={action.onClick}>
      {button}
    </Popconfirm>
  ) : button;
}
