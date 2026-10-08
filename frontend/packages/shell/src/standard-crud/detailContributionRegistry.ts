import type { FrontendModule, StandardCrudDetailContribution } from '@icp-platform/sdk';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';

export interface ResolvedDetailContribution extends StandardCrudDetailContribution {
  titleKey: string;
}

/** 贡献来自活动模块的真实 List View；目标详情与模型、来源导航和可执行绑定在发布候选时统一核对。 */
export function resolveDetailContributions(modules: FrontendModule[], metadata: RuntimeMetadataState): ResolvedDetailContribution[] {
  const result: ResolvedDetailContribution[] = [];
  const keys = new Set<string>();
  const targetsBySource = new Set<string>();
  const ownsList = (module: FrontendModule, key: string) => module.standardCrudCapabilities?.some(binding => binding.kind === 'standard-list' && binding.targetKey === key)
    || module.standardCrudResources?.some(resource => resource.listViewKey === key);
  for (const document of metadata.viewDocuments) {
    const view = document.view;
    if (view.detailContributions === undefined) continue;
    if (view.mode !== 'list' || !Array.isArray(view.detailContributions)) throw new Error('详情贡献必须属于 List View');
    const owners = modules.filter(module => ownsList(module, String(view.key)));
    if (owners.length !== 1) throw new Error('详情贡献来源 binding 不唯一');
    for (const value of view.detailContributions) {
      const entry = value as StandardCrudDetailContribution;
      if (typeof entry?.key !== 'string' || !entry.key.trim() || keys.has(entry.key)
        || typeof entry.targetViewKey !== 'string' || !entry.targetViewKey.trim()
        || typeof entry.navigationKey !== 'string' || !entry.navigationKey.trim()) throw new Error('详情贡献 key/target/navigation 缺失或重复');
      keys.add(entry.key);
      const sourceTarget = JSON.stringify([view.key, entry.targetViewKey]);
      if (targetsBySource.has(sourceTarget)) throw new Error('详情贡献目标重复');
      targetsBySource.add(sourceTarget);
      const targets = metadata.viewDocuments.filter(item => item.view.key === entry.targetViewKey && item.view.mode === 'detail');
      const targetOwners = modules.filter(module => module.standardCrudCapabilities?.some(binding =>
        binding.kind === 'standard-crud-detail' && binding.targetKey === entry.targetViewKey)
        || module.standardCrudResources?.some(resource => resource.detailViewKey === entry.targetViewKey)
        || metadata.viewDocuments.some(item => item.view.mode === 'list' && item.view.detailViewKey === entry.targetViewKey && ownsList(module, String(item.view.key))));
      if (targets.length !== 1 || targetOwners.length !== 1) throw new Error('详情贡献目标 detail binding 不唯一');
      const targetModel = String(targets[0].view.model);
      const modelKey = targetModel.includes('/') ? targetModel : `${targetOwners[0].metadata.moduleId}/${targetModel}`;
      if (entry.pageRef?.pageKey !== 'platform.list' || entry.pageRef.parameters?.viewKey !== view.key
        || entry.pageRef.parameters?.anchorModelKey !== modelKey || 'recordId' in entry.pageRef.parameters || !entry.pageRef.inputResolverKey
        || !owners[0].pageInputResolvers?.some(resolver => resolver.key === entry.pageRef.inputResolverKey && resolver.pageKeys.includes('platform.list')))
        throw new Error('详情贡献 PageRef 或目标模型不匹配');
      const navs = metadata.navDocuments.flatMap(item => item.navigation.items).filter(nav => nav.key === entry.navigationKey);
      const ownerMetadata = metadata.manifest.modules.find(module => module.moduleId === owners[0].metadata.moduleId);
      if (navs.length !== 1 || !navs[0].featurePosition || !ownerMetadata?.features?.some(feature => feature.position === navs[0].featurePosition && feature.kind === 'navigation'))
        throw new Error('详情贡献来源导航不属于 owner');
      if (typeof view.dicKey !== 'string' || typeof view.titleKey !== 'string') throw new Error('详情贡献缺少标题');
      result.push({ ...entry, titleKey: `${view.dicKey}.${view.titleKey}` });
    }
  }
  return result;
}
