import { useFeaturePlacement } from '../authorization/FeaturePlacementContext';
import { useContext, useEffect, useId, useState } from 'react';
import Tabs from 'antd/es/tabs';
import { useLocalization } from '@icp-platform/core';
import type { PageProps, TabView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext, PageNavigationContext, TreeNavigationContext } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';
import { createTabLocationMemory } from './compositionNavigation';
import { resolvedFeaturePosition } from '../authorization/featurePlacementResolver';

/** 已打开的标签保持挂载，未激活页面仍参与当前组合保护。 */
export default function StandardTabPage({ input }: PageProps) {
  const metadata = useRuntimeMetadata();
  const features = useFeaturePlacement();
  const runtime = useContext(CompositionContext)!;
  const { t } = useLocalization();
  const view = metadata?.viewDocuments.find(document => document.view.key === input.parameters.viewKey)?.view as unknown as TabView;
  const navigation = useContext(PageNavigationContext);
  const treeNavigation = useContext(TreeNavigationContext);
  const instanceId = useId();
  const id = `np-tabs-${instanceId.replace(/:/g, '')}`;
  const resolvedTabs = view.tabs.map(tab => ({ tab, resolution: features.tab(tab.key) }));
  const availableTabs = resolvedTabs.filter(({ resolution }) => !features.placement || resolution.kind === 'allowed');
  const fallback = availableTabs.find(({ tab }) => tab.key === view.defaultTabKey)?.tab.key ?? availableTabs[0]?.tab.key ?? "";
  const [localSelected, setSelected] = useState(view.defaultTabKey);
  const requested = navigation?.tabKey ?? localSelected;
  const selected = availableTabs.some(({ tab }) => tab.key === requested) ? requested : fallback;
  const [treeLocations] = useState(createTabLocationMemory);
  const tabTreeNavigation = treeLocations.forSelection(selected, treeNavigation);
  const [opened, setOpened] = useState(() => new Set([selected]));
  useEffect(() => { setOpened(current => current.has(selected) ? current : new Set(current).add(selected)); }, [selected]);
  const visibleTabs = new Set([...opened, selected]);
  return <Tabs id={id} className="np-composed-tabs" type={input.context ? 'line' : 'card'} size="small" activeKey={selected} destroyOnHidden={false} animated={false}
    onChange={key => {
      if (selected === key) return;
      if (navigation) { setOpened(current => new Set(current).add(selected)); navigation.selectTab(key, tabTreeNavigation.handoff(key)); }
      else runtime.leave(() => { setSelected(key); setOpened(current => new Set(current).add(key)); });
    }}
    items={availableTabs.map(({ tab, resolution }) => ({
      key: tab.key,
      label: t(`${view.dicKey}.${tab.titleKey}`, tab.titleKey),
      forceRender: visibleTabs.has(tab.key),
      children: <PageOutlet key={input.context?.nodeKey} page={tab.pageRef} featurePlacement={features.placement ? { entryPosition: resolvedFeaturePosition(resolution)! } : undefined} context={input.context} active={tab.key === selected} treeNavigation={tabTreeNavigation.forTab(tab.key)} />,
    }))} />;
}
