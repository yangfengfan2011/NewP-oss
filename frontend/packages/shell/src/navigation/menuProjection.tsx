import {
  ApartmentOutlined,
  AppstoreOutlined,
  AuditOutlined,
  BranchesOutlined,
  ControlOutlined,
  DatabaseOutlined,
  ExperimentOutlined,
  FileTextOutlined,
  FolderOutlined,
  GlobalOutlined,
  HomeOutlined,
  IdcardOutlined,
  KeyOutlined,
  SafetyCertificateOutlined,
  SettingOutlined,
  TeamOutlined,
  ToolOutlined,
  UnorderedListOutlined,
  UserOutlined,
} from '@ant-design/icons';
import type { MenuProps } from 'antd';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { MenuConfig } from '@icp-platform/sdk';

type AntdMenuItems = Required<MenuProps>['items'];

const menuIconMap: Record<string, ReactNode> = {
  app: <AppstoreOutlined />,
  appstore: <AppstoreOutlined />,
  audit: <AuditOutlined />,
  authorization: <SafetyCertificateOutlined />,
  branches: <BranchesOutlined />,
  clipboard: <FileTextOutlined />,
  'clipboard-list': <FileTextOutlined />,
  cog: <SettingOutlined />,
  config: <SettingOutlined />,
  control: <ControlOutlined />,
  database: <DatabaseOutlined />,
  demo: <ExperimentOutlined />,
  document: <FileTextOutlined />,
  'document-text': <FileTextOutlined />,
  files: <FolderOutlined />,
  folder: <FolderOutlined />,
  globe: <GlobalOutlined />,
  'globe-alt': <GlobalOutlined />,
  grid: <AppstoreOutlined />,
  home: <HomeOutlined />,
  key: <KeyOutlined />,
  list: <UnorderedListOutlined />,
  metadata: <DatabaseOutlined />,
  module: <AppstoreOutlined />,
  operation: <FileTextOutlined />,
  organization: <ApartmentOutlined />,
  person: <UserOutlined />,
  persons: <TeamOutlined />,
  permission: <KeyOutlined />,
  plugin: <ToolOutlined />,
  position: <IdcardOutlined />,
  role: <TeamOutlined />,
  server: <ToolOutlined />,
  settings: <SettingOutlined />,
  shield: <SafetyCertificateOutlined />,
  tree: <BranchesOutlined />,
  user: <UserOutlined />,
  'user-group': <TeamOutlined />,
  'view-grid': <AppstoreOutlined />,
};

export function projectMenusToAntdItems(
  menus: MenuConfig[],
  canViewMenu: (menu: MenuConfig) => boolean,
): AntdMenuItems {
  return menus.flatMap((menu) => {
    if (!canViewMenu(menu)) {
      return [];
    }

    const children = menu.children
      ? projectMenusToAntdItems(menu.children, canViewMenu)
      : undefined;

    return [{
      key: menu.path ?? menu.id,
      icon: resolveMenuIcon(menu),
      label: menu.path ? <Link to={menu.path}>{menu.label}</Link> : menu.label,
      children: children && children.length > 0 ? children : undefined,
    }];
  });
}

function resolveMenuIcon(menu: MenuConfig): ReactNode {
  const iconKey = menu.icon ?? inferMenuIconKey(menu);
  if (!iconKey) {
    return undefined;
  }

  const normalizedIconKey = iconKey.toLowerCase();
  const mappedIcon = menuIconMap[normalizedIconKey];
  if (mappedIcon) {
    return <span className="np-sidebar-menu-icon">{mappedIcon}</span>;
  }

  // 菜单图标属于导航展示元数据；未知值只做轻量文本回退，避免元数据小误差打断 Shell 渲染。
  return <span className="np-sidebar-menu-icon">{iconKey}</span>;
}

function inferMenuIconKey(menu: MenuConfig): string | undefined {
  const rawKey = `${menu.id} ${menu.label}`.toLowerCase();

  if (rawKey.includes('组织树')) return 'tree';
  if (rawKey.includes('position') || rawKey.includes('岗位')) return 'position';
  if (rawKey.includes('person') || rawKey.includes('人员')) return 'persons';
  if (rawKey.includes('organization') || rawKey.includes('组织结构')) return 'organization';
  if (rawKey.includes('plugin') || rawKey.includes('插件')) return 'plugin';
  if (rawKey.includes('file') || rawKey.includes('文件')) return 'files';
  if (rawKey.includes('demo') || rawKey.includes('演示')) return 'demo';
  if (rawKey.includes('template') || rawKey.includes('模板')) return 'module';
  if (rawKey.includes('showcase') || rawKey.includes('控件')) return 'control';
  if (rawKey.includes('config') || rawKey.includes('配置')) return 'settings';
  if (rawKey.includes('authorization') || rawKey.includes('授权')) return 'authorization';
  if (rawKey.includes('audit') || rawKey.includes('日志')) return 'audit';
  if (rawKey.includes('metadata') || rawKey.includes('元数据')) return 'metadata';

  return undefined;
}

export function collectMenuPaths(menus: MenuConfig[]): Array<{ id: string; label: string; path?: string; parentIds: string[] }> {
  const entries: Array<{ id: string; label: string; path?: string; parentIds: string[] }> = [];

  const visit = (items: MenuConfig[], parentIds: string[]) => {
    for (const item of items) {
      entries.push({
        id: item.id,
        label: item.label,
        path: item.path,
        parentIds,
      });

      if (item.children) {
        visit(item.children, [...parentIds, item.id]);
      }
    }
  };

  visit(menus, []);
  return entries;
}
