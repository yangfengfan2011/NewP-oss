import type { FrontendModule, PageInput, PageRef, TabView, TreeView } from '@icp-platform/sdk';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { relationTreeProviderKey } from './relationTreeProvider';
import { resolveDetailContributions } from '../standard-crud/detailContributionRegistry';
import { resolveRowContextMenu } from '../metadata/standardCrudSchemaResolver';

const modes: Record<string, string> = { 'platform.tree': 'tree', 'platform.tabs': 'tab', 'platform.list': 'list', 'platform.detail': 'detail' };
export function resolvePageInput(ref: PageRef, modules: FrontendModule[], context?: PageInput['context']): PageInput {
  const parameters = ref.parameters ?? {};
  const resolver = modules.flatMap(module => module.pageInputResolvers ?? []).find(binding => binding.key === ref.inputResolverKey);
  if (ref.inputResolverKey && (!resolver || !resolver.pageKeys.includes(ref.pageKey))) throw new Error(`页面输入解析器不兼容：${ref.inputResolverKey}`);
  const input = resolver ? resolver.resolve(parameters, context) : { parameters, context };
  if (modes[ref.pageKey] && typeof input.parameters.viewKey !== 'string') throw new Error('页面需要 viewKey 参数');
  if (ref.pageKey === 'platform.tree' && !input.treeScope) throw new Error('树页面缺少必需 treeScope');
  if (ref.pageKey === 'platform.detail' && !input.recordId) throw new Error('详情页面缺少必需上下文');
  const binding = modules.flatMap(module => module.pages ?? []).find(page => page.pageKey === ref.pageKey);
  if (!modes[ref.pageKey] && !binding) throw new Error(`未注册页面：${ref.pageKey}`);
  for (const key of binding?.requiredParameters ?? []) if (!(key in input.parameters)) throw new Error(`页面缺少参数：${key}`);
  if (binding?.requiresRecordId && !input.recordId) throw new Error('页面缺少记录上下文');
  return input;
}
/** 使用当前配置链检测循环；相同页面的不同配置与兄弟复用均合法。 */
export function validateCompositionCandidate(modules: FrontendModule[], metadata: RuntimeMetadataState): void {
  for (const module of modules) {
    for (const resolver of module.pageInputResolvers ?? []) if (!resolver.key || !resolver.pageKeys.length || typeof resolver.resolve !== 'function') throw new Error('无效页面输入 binding');
    for (const page of module.pages ?? []) if (!page.pageKey || typeof page.load !== 'function') throw new Error('无效页面 binding');
    for (const provider of module.treeProviders ?? []) if (['roots', 'children', 'path', 'search', 'resolveChange'].some(key => typeof provider[key as keyof typeof provider] !== 'function')) throw new Error('不完整树 Provider binding');
  }
  const views = new Map(metadata.viewDocuments.map(document => [String(document.view.key), document.view]));
  if (views.size !== metadata.viewDocuments.length) throw new Error('重复 View key');
  for (const [key, view] of views) if (view.mode === 'list' || view.mode === undefined) {
    resolveRowContextMenu(view.rowContextMenu, view.rowActions as { key: string }[] ?? [], key);
  }
  const check = (ref: PageRef, chain: Set<string>) => {
    if (!ref || typeof ref.pageKey !== 'string') throw new Error('缺少 PageRef.pageKey');
    const resolver = modules.flatMap(module => module.pageInputResolvers ?? []).find(item => item.key === ref.inputResolverKey);
    if (ref.inputResolverKey && (!resolver || !resolver.pageKeys.includes(ref.pageKey))) throw new Error(`无效 inputResolverKey：${ref.inputResolverKey}`);
    if (!modes[ref.pageKey]) {
      const page = modules.flatMap(module => module.pages ?? []).find(item => item.pageKey === ref.pageKey);
      if (!page) throw new Error(`未注册页面：${ref.pageKey}`);
      for (const key of page.requiredParameters ?? []) if (!(key in (ref.parameters ?? {}))) throw new Error(`页面缺少参数：${key}`);
      return;
    }
    const key = ref.parameters?.viewKey;
    const view = typeof key === 'string' ? views.get(key) : undefined;
    if (!view || view.mode !== modes[ref.pageKey]) throw new Error(`页面 View 参数不兼容：${String(key)}`);
    if (chain.has(String(key))) throw new Error(`循环页面配置：${key}`);
    const next = new Set(chain).add(String(key));
    if (view.mode === 'tab') {
      const tab = view as unknown as TabView;
      if (!tab.tabs.length || new Set(tab.tabs.map(item => item.key)).size !== tab.tabs.length
        || !tab.tabs.some(item => item.key === tab.defaultTabKey)) throw new Error('无效默认标签或重复标签');
      for (const item of tab.tabs) { if (!item.titleKey) throw new Error('标签缺少 titleKey'); check(item.pageRef, next); }
    }
    if (view.mode === 'tree') {
      const tree = view as unknown as TreeView;
      if (tree.searchEnabled !== undefined && typeof tree.searchEnabled !== 'boolean') throw new Error('searchEnabled 必须为布尔值');
      if (tree.providerKey === relationTreeProviderKey) {
        if (!tree.relationSource?.nodes.length
          || tree.relationSource.nodes.length !== tree.nodeTypes.length
          || new Set(tree.relationSource.nodes.map(node => node.key)).size !== tree.nodeTypes.length
          || tree.relationSource.nodes.some(node => !tree.nodeTypes.some(type => type.key === node.key))) throw new Error('无效标准关系树配置');
      } else if (!modules.some(module => module.treeProviders?.some(provider => provider.key === tree.providerKey))) throw new Error('缺少树 Provider binding');
      if (!tree.nodeTypes.length || new Set(tree.nodeTypes.map(item => item.key)).size !== tree.nodeTypes.length) throw new Error('重复或缺少节点类型');
      const checkActions = (actions: TreeView['treeActions']) => {
        if (!actions) return;
        if (!Array.isArray(actions) || new Set(actions.map(action => action.key)).size !== actions.length) throw new Error('无效树动作数组或重复 key');
        for (const action of actions) {
          if (!action.key || !action.labelKey || Boolean(action.actionKey) === Boolean(action.standardAction)) throw new Error('树动作需要唯一的动作引用');
          check({ pageKey: 'platform.list', parameters: { viewKey: action.viewKey }, inputResolverKey: action.inputResolverKey }, next);
          const target = views.get(action.viewKey)!;
          const definitions = [...(target.pageActions as Array<Record<string, unknown>> ?? []), ...(target.rowActions as Array<Record<string, unknown>> ?? []),
            ...(target.featureActions as Array<Record<string, unknown>> ?? []).filter(item => item.handlerKey)];
          const matches = definitions.filter(item => action.actionKey ? item.key === action.actionKey : item.standardAction === action.standardAction);
          if (matches.length !== 1) throw new Error('树动作引用不存在或不唯一');
          const registered = modules.flatMap(module => module.standardCrudResources ?? []).find(resource => resource.listViewKey === action.viewKey);
          const capability = modules.flatMap(module => module.standardCrudCapabilities ?? []).find(item => item.kind === 'standard-list' && item.targetKey === action.viewKey);
          if (!registered && !capability) throw new Error('树动作缺少标准资源 binding');
          const definition = matches[0];
          const handlerKey = definition.handlerKey ?? (!definition.standardAction ? definition.key : undefined);
          if (registered && ((handlerKey && !registered.actionHandlers?.[String(handlerKey)])
            || (definition.inputKey && !registered.actionInputRenderers?.[String(definition.inputKey)]))) throw new Error('树动作 handler/input binding 不匹配');
        }
      };
      checkActions(tree.treeActions);
      for (const item of tree.nodeTypes) {
        if (!item.titleKey) throw new Error('节点类型缺少 titleKey');
        check(item.pageRef, next); checkActions(item.contextMenu);
      }
    }
  };
  for (const view of views.values()) if (view.mode === 'tree' || view.mode === 'tab')
    check({ pageKey: view.mode === 'tree' ? 'platform.tree' : 'platform.tabs', parameters: { viewKey: view.key } }, new Set());
  for (const nav of metadata.navDocuments.flatMap(document => document.navigation.items)) if (nav.page) check(nav.page, new Set());
  for (const contribution of resolveDetailContributions(modules, metadata)) check(contribution.pageRef, new Set());
}
