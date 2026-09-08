import { useContext, useEffect, useId, useState } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { PageProps, TabView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext, PageNavigationContext } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';

/** 已打开的标签保持挂载，未激活页面仍参与当前组合保护。 */
export default function StandardTabPage({ input }: PageProps) {
  const metadata = useRuntimeMetadata();
  const runtime = useContext(CompositionContext)!;
  const { t } = useLocalization();
  const view = metadata?.viewDocuments.find(document => document.view.key === input.parameters.viewKey)?.view as unknown as TabView;
  const navigation = useContext(PageNavigationContext);
  const id = useId();
  const [localSelected, setSelected] = useState(view.defaultTabKey);
  const selected = navigation ? (view.tabs.some(tab => tab.key === navigation.tabKey) ? navigation.tabKey! : view.defaultTabKey) : localSelected;
  const [opened, setOpened] = useState(() => new Set([selected]));
  useEffect(() => { setOpened(current => current.has(selected) ? current : new Set(current).add(selected)); }, [selected]);
  const visibleTabs = new Set([...opened, selected]);
  return <div>
    <div role="tablist" style={{ display: 'flex', gap: 8, borderBottom: '1px solid #ddd', marginBottom: 16 }}>
      {view.tabs.map(tab => <button key={tab.key} role="tab" id={`${id}-${tab.key}`} aria-controls={`${id}-panel-${tab.key}`} aria-selected={selected === tab.key}
        onClick={() => {
          if (selected === tab.key) return;
          if (navigation) { setOpened(current => new Set(current).add(selected)); navigation.selectTab(tab.key); }
          else runtime.leave(() => { setSelected(tab.key); setOpened(current => new Set(current).add(tab.key)); });
        }}>
        {t(`${view.dicKey}.${tab.titleKey}`, tab.titleKey)}
      </button>)}
    </div>
    {view.tabs.filter(tab => visibleTabs.has(tab.key)).map(tab => <div key={tab.key} role="tabpanel" id={`${id}-panel-${tab.key}`} aria-labelledby={`${id}-${tab.key}`} hidden={tab.key !== selected}>
      <PageOutlet key={input.context?.nodeKey} page={tab.pageRef} context={input.context} active={tab.key === selected} />
    </div>)}
  </div>;
}
