import { useContext, useEffect, useId, useRef, useState } from 'react';
import Tabs from 'antd/es/tabs';
import { useLocalization } from '@icp-platform/core';
import type { PageProps, TabView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext, PageNavigationContext, TreeNavigationContext, type TreeLocation } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';

/** 已打开的标签保持挂载，未激活页面仍参与当前组合保护。 */
export default function StandardTabPage({ input }: PageProps) {
  const metadata = useRuntimeMetadata();
  const runtime = useContext(CompositionContext)!;
  const { t } = useLocalization();
  const view = metadata?.viewDocuments.find(document => document.view.key === input.parameters.viewKey)?.view as unknown as TabView;
  const navigation = useContext(PageNavigationContext);
  const treeNavigation = useContext(TreeNavigationContext);
  const id = useId();
  const [localSelected, setSelected] = useState(view.defaultTabKey);
  const selected = navigation ? (view.tabs.some(tab => tab.key === navigation.tabKey) ? navigation.tabKey! : view.defaultTabKey) : localSelected;
  // URL 只描述当前标签；已打开兄弟树的位置留在本 Tab 实例，不能消费另一个标签的地址。
  const treeLocations = useRef(new Map<string, TreeLocation>());
  if (treeNavigation) treeLocations.current.set(selected, { nodeKey: treeNavigation.nodeKey, nodeTab: treeNavigation.nodeTab });
  const [opened, setOpened] = useState(() => new Set([selected]));
  useEffect(() => { setOpened(current => current.has(selected) ? current : new Set(current).add(selected)); }, [selected]);
  const visibleTabs = new Set([...opened, selected]);
  return <Tabs id={id} activeKey={selected} destroyOnHidden={false} animated={false}
    onChange={key => {
      if (selected === key) return;
      if (navigation) { setOpened(current => new Set(current).add(selected)); navigation.selectTab(key, treeNavigation ? treeLocations.current.get(key) ?? { nodeKey: null, nodeTab: null } : undefined); }
      else runtime.leave(() => { setSelected(key); setOpened(current => new Set(current).add(key)); });
    }}
    items={view.tabs.map(tab => ({
      key: tab.key,
      label: t(`${view.dicKey}.${tab.titleKey}`, tab.titleKey),
      forceRender: visibleTabs.has(tab.key),
      children: <PageOutlet key={input.context?.nodeKey} page={tab.pageRef} context={input.context} active={tab.key === selected} treeNavigation={treeNavigation ? {
        ...(treeLocations.current.get(tab.key) ?? { nodeKey: null, nodeTab: null }),
        locate: (nodeKey, nodeTab) => {
          treeLocations.current.set(tab.key, { nodeKey, nodeTab: nodeTab === undefined ? treeLocations.current.get(tab.key)?.nodeTab ?? null : nodeTab });
          if (tab.key === selected) treeNavigation.locate(nodeKey, nodeTab);
        },
      } : null} />,
    }))} />;
}
