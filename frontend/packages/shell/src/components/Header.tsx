import { LogoutOutlined, UserOutlined } from '@ant-design/icons';
import { Avatar, Button, Layout as AntdLayout, Segmented, Space, Typography } from 'antd';
import { useAuth } from '../contexts/AuthContext';
import { usePlatform } from '../contexts/PlatformContext';

const { Header: AntdHeader } = AntdLayout;
const { Text } = Typography;

export function Header() {
  const auth = useAuth();
  const { shellView, setShellView, canSwitchShellView } = usePlatform();

  return (
    <AntdHeader className="np-app-header">
      <div className="np-header-brand">
        <span className="np-header-logo">ICP</span>
        <span className="np-header-title">ICP Platform</span>
        <span className="np-header-divider" />
        <Text className="np-header-meta">统一业务与平台工作台</Text>
      </div>
      <Space size={16} className="np-header-actions">
        {canSwitchShellView && (
          <Segmented
            size="small"
            value={shellView}
            onChange={(value) => setShellView(value as 'business' | 'platform')}
            options={[
              { label: '业务视图', value: 'business' },
              { label: '平台视图', value: 'platform' },
            ]}
          />
        )}
        {auth.isAuthenticated && auth.user && (
          <Space size={12}>
            <Avatar size="small" icon={<UserOutlined />} />
            <Text className="np-header-user">欢迎，{auth.user.displayName}</Text>
            <Button type="text" size="small" icon={<LogoutOutlined />} onClick={() => auth.logout()}>
              退出登录
            </Button>
          </Space>
        )}
      </Space>
    </AntdHeader>
  );
}
