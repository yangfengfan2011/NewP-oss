import type { ThemeConfig } from 'antd';

export const shellAntdTheme: ThemeConfig = {
  token: {
    colorPrimary: '#1677ff',
    colorSuccess: '#52c41a',
    colorWarning: '#faad14',
    colorError: '#ff4d4f',
    colorInfo: '#1677ff',
    borderRadius: 6,
    fontSize: 14,
    colorBgLayout: '#f5f5f5',
    colorBgContainer: '#ffffff',
  },
  components: {
    Layout: {
      headerBg: '#ffffff',
      siderBg: '#001529',
      bodyBg: '#f5f5f5',
      headerHeight: 56,
    },
    Menu: {
      itemBorderRadius: 4,
      itemHeight: 40,
    },
    Table: {
      headerBg: '#fafafa',
      rowHoverBg: '#f5faff',
    },
  },
};
