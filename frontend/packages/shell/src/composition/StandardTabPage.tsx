import { useContext, useState } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { PageProps, TabView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';

/** 已打开的标签保持挂载，未激活页面仍参与当前组合保护。 */
export default function StandardTabPage({ input }: PageProps) {
  const metadata = useRuntimeMetadata();
  const runtime = useContext(CompositionContext)!;
  const { t } = useLocalization();
  const view = metadata?.viewDocuments.find(document => document.view.key === input.parameters.viewKey)?.view as unknown as TabView;
  const [selected, setSelected] = useState(view.defaultTabKey);
  const [opened, setOpened] = useState(() => new Set([view.defaultTabKey]));
  return <div>
    <div role="tablist" style={{ display: 'flex', gap: 8, borderBottom: '1px solid #ddd', marginBottom: 16 }}>
      {view.tabs.map(tab => <button key={tab.key} role="tab" aria-selected={selected === tab.key}
        onClick={() => runtime.leave(() => { setSelected(tab.key); setOpened(current => new Set(current).add(tab.key)); })}>
        {t(`${view.dicKey}.${tab.titleKey}`, tab.titleKey)}
      </button>)}
    </div>
    {view.tabs.filter(tab => opened.has(tab.key)).map(tab => <div key={tab.key} role="tabpanel" hidden={tab.key !== selected}>
      <PageOutlet page={tab.pageRef} context={input.context} />
    </div>)}
  </div>;
}
