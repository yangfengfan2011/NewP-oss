import type { FrontendModule, StandardCrudResourceRegistration } from '@icp-platform/sdk';

/** 组合页面与动作预览沿同一个已声明list绑定装载注册，不执行业务动作或输入准备。 */
export function loadStandardListRegistration(modules: readonly FrontendModule[], listViewKey: unknown): Promise<{ default: StandardCrudResourceRegistration }> | undefined {
  if (typeof listViewKey !== 'string' || !listViewKey) return undefined;
  const registered = modules.flatMap(module => module.standardCrudResources ?? []).find(resource => resource.listViewKey === listViewKey);
  const binding = modules.flatMap(module => module.standardCrudCapabilities ?? []).find(item => item.kind === 'standard-list' && item.targetKey === listViewKey);
  return registered ? Promise.resolve({ default: registered }) : binding?.load();
}
