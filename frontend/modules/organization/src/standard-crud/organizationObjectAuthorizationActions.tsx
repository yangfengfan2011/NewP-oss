import { createObjectAuthorizationActions } from '@icp-platform/ui';

/** 标准资源只提供当前行身份和名称，动作资格、编辑与查看输入共用通用注册。 */
export function createOrganizationObjectAuthorizationActions(resourceKey: string, title: string) {
  return createObjectAuthorizationActions(resourceKey, context => {
    const row = context.row as { id?: string; EID?: string; ENAME?: string } | undefined;
    return { title, id: row?.id ?? row?.EID, name: row?.ENAME };
  });
}