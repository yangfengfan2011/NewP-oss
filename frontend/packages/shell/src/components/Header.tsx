import { LogoutOutlined, UserOutlined } from '@ant-design/icons';
import { Avatar, Button, Layout as AntdLayout, Space, Typography } from 'antd';
import { useAuth } from '../contexts/AuthContext';

const { Header: AntdHeader } = AntdLayout;
const { Text } = Typography;

export function Header() {
  const auth = useAuth();

  return (
    <AntdHeader className="np-app-header">
      <div className="np-header-brand">
        <span className="np-header-logo">ICP</span>
      </div>
      <Space size={16} className="np-header-actions">
        {auth.isAuthenticated && auth.user && (
          <Space size={12}>
            <Avatar size="small" icon={<UserOutlined />} />
            <Text className="np-header-user">欢迎，{auth.user.ename}</Text>
            <Button type="text" size="small" icon={<LogoutOutlined />} onClick={() => void auth.logout().catch(() => undefined)}>
              退出登录
            </Button>
          </Space>
        )}
      </Space>
    </AntdHeader>
  );
}
