import type { ThemeConfig } from 'antd/es/config-provider/context';

export const shellAntdTheme: ThemeConfig = {
  token: {
    colorPrimary: '#1677ff',
    colorSuccess: '#52c41a',
    colorWarning: '#faad14',
    colorError: '#ff4d4f',
    colorInfo: '#1677ff',
    borderRadius: 6,
    controlHeight: 32,
    fontSize: 14,
    colorBgLayout: '#f6f8fa',
    colorBgContainer: '#ffffff',
    colorText: '#111827',
    colorTextSecondary: '#64748b',
    colorBorder: '#e5e7eb',
    colorBorderSecondary: '#eef2f7',
  },
  components: {
    Layout: {
      headerBg: '#ffffff',
      siderBg: '#ffffff',
      bodyBg: '#f6f8fa',
      headerHeight: 52,
    },
    Menu: {
      itemBorderRadius: 6,
      itemHeight: 40,
      itemSelectedBg: '#eff6ff',
      itemSelectedColor: '#1677ff',
      itemColor: '#475569',
      itemHoverBg: '#f8fafc',
      itemHoverColor: '#1677ff',
      subMenuItemBg: '#ffffff',
    },
    Table: {
      headerBg: '#f8fafc',
      headerColor: '#334155',
      rowHoverBg: '#f9fafb',
      cellPaddingBlock: 12,
      cellPaddingInline: 16,
    },
    Button: {
      controlHeight: 32,
      borderRadius: 6,
    },
    Input: {
      controlHeight: 32,
      borderRadius: 6,
    },
    Select: {
      controlHeight: 32,
      borderRadius: 6,
    },
    Card: {
      borderRadiusLG: 8,
    },
  },
};
