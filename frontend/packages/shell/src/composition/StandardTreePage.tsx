import { useFeaturePlacement, type FeaturePlacement } from '../authorization/FeaturePlacementContext';
import { useContext, useId, useMemo, useState } from 'react';
import Tree from 'antd/es/tree';
import type { DataNode } from 'antd/es/tree';
import Dropdown from 'antd/es/dropdown';
import Button from 'antd/es/button';
import Card from 'antd/es/card';
import ConfigProvider from 'antd/es/config-provider';
import Space from 'antd/es/space';
import PlusOutlined from '@ant-design/icons/PlusOutlined';
import DownOutlined from '@ant-design/icons/DownOutlined';
import theme from 'antd/es/theme';
import { useLocalization } from '@icp-platform/core';
import { ModuleRegistry, type PageProps, type PageRef, type TreeNodeDescriptor, type TreeView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext, TreeInstanceContext, TreeNavigationContext } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';
import { useTreeReadLifecycle } from './useTreeReadLifecycle';
import { TreeSearch } from './TreeSearch';
import { createRelationTreeProvider, relationTreeProviderKey } from './relationTreeProvider';
import { resolvedFeaturePosition } from '../authorization/featurePlacementResolver';

/** 公共树管理页面只消费 Provider 节点与 PageRef，不解释业务标识或组织字段。 */
export default function StandardTreePage({ input, active = true }: PageProps) {
  const metadata = useRuntimeMetadata();
  const features = useFeaturePlacement();
  const readPosition = resolvedFeaturePosition(features.read());
  const [editFeature, setEditFeature] = useState<FeaturePlacement | undefined>();
  const runtime = useContext(CompositionContext)!;
  const navigation = useContext(TreeNavigationContext);
  const instanceId = useId();
  const { t } = useLocalization();
  const { token } = theme.useToken();
  const view = metadata?.viewDocuments.find(document => document.view.key === input.parameters.viewKey)?.view as unknown as TreeView;
  const treeScope = input.treeScope!;
  const provider = useMemo(() => view.providerKey === relationTreeProviderKey ? createRelationTreeProvider(view, readPosition)
    : ModuleRegistry.getAllModules().flatMap(module => module.treeProviders ?? []).find(item => item.key === view.providerKey)!, [view, readPosition]);
  const [menuNode, setMenuNode] = useState<string | null>(null);
  const [editContext, setEditContext] = useState<TreeNodeDescriptor | undefined>();
  const [editPage, setEditPage] = useState<PageRef | null>(null);
  const tree = useTreeReadLifecycle({
    provider, scope: treeScope, instanceId, active, runtime, navigation,
    onSelectAccepted: () => setEditPage(null),
  });
  const { roots, branches, selected, expanded } = tree.snapshot;
  const data = (nodes: readonly TreeNodeDescriptor[]): DataNode[] => nodes.map(node => {
    const type = view.nodeTypes.find(item => item.key === node.nodeType);
    const title = <span style={{ whiteSpace: 'normal', overflowWrap: 'anywhere' }} title={type ? t(`${view.dicKey}.${type.titleKey}`, type.titleKey) : ''}>{node.label}{node.isEnabled === false ? '（停用）' : ''}</span>;
    return { key: node.nodeKey, isLeaf: branches[node.nodeKey] ? branches[node.nodeKey].length === 0 : !node.hasChildren,
      title: <Dropdown trigger={['contextMenu']} open={menuNode === node.nodeKey} menu={{ items: (type?.contextMenu ?? []).map(menu => ({ menu, resolution: features.action(menu.key, 'contextMenu', node.nodeType) })).filter(({ resolution }) => !features.placement || resolution.kind === 'allowed').map(({ menu, resolution }) => ({ key: menu.key, label: t(`${view.dicKey}.${menu.labelKey}`, menu.labelKey), onClick: () => tree.select(node.nodeKey, () => { setMenuNode(null); setEditFeature(features.placement ? { ...features.placement, staticScope: node.nodeType, actionPosition: resolvedFeaturePosition(resolution), actionKey: menu.actionKey ?? menu.standardAction } : undefined); setEditContext(node); setEditPage({ pageKey: 'platform.list', parameters: { viewKey: menu.viewKey, presentation: 'drawer', standardAction: menu.standardAction, actionKey: menu.actionKey }, inputResolverKey: menu.inputResolverKey }); }) })) }}
        onOpenChange={open => { if (open) tree.select(node.nodeKey, () => setMenuNode(node.nodeKey)); else setMenuNode(null); }}>{title}</Dropdown>,
      children: branches[node.nodeKey] ? data(branches[node.nodeKey]) : undefined };
  });
  const selectedType = view.nodeTypes.find(type => type.key === selected?.nodeType);
  return <TreeInstanceContext.Provider value={instanceId}><div className="np-tree-workspace">
    <aside aria-label="结构树" style={{ minWidth: 0 }}>
      <Card size="small" className="np-tree-sidebar-card"
        styles={{ header: { minHeight: token.controlHeightLG, padding: `${token.paddingXXS}px ${token.paddingSM}px` }, body: { padding: token.paddingSM } }}
        title={view.treeActions?.length ? <Space role="toolbar" aria-label="树操作" size={token.marginXS} style={{ width: '100%' }} wrap>
          {view.treeActions.map(action => ({ action, resolution: features.action(action.key, 'treeActions') })).filter(({ resolution }) => !features.placement || resolution.kind === 'allowed').map(({ action, resolution }) => <Button key={action.key} size="small" icon={action.standardAction === 'create' ? <PlusOutlined aria-hidden="true" /> : undefined}
            onClick={() => runtime.leave(() => { setEditContext(undefined); setEditFeature(features.placement ? { ...features.placement, actionPosition: resolvedFeaturePosition(resolution), actionKey: action.actionKey ?? action.standardAction } : undefined); setEditPage({ pageKey: 'platform.list', parameters: { viewKey: action.viewKey, presentation: 'drawer', standardAction: action.standardAction, actionKey: action.actionKey }, inputResolverKey: action.inputResolverKey }); })}>{t(`${view.dicKey}.${action.labelKey}`, action.labelKey)}</Button>)}
        </Space> : undefined}>
      <TreeSearch provider={provider} scope={treeScope} view={view} choose={tree.locate} />
      {tree.error && <div role="alert">树读取失败：{tree.error}<Button onClick={() => tree.retry({ kind: 'tree' })}>重试</Button></div>}
      {tree.locationError && <div role="alert">定位读取失败：{tree.locationError}<Button onClick={() => tree.retry({ kind: 'location' })}>重试定位</Button></div>}
      {tree.locationNotice && <div role="status">{tree.locationNotice}</div>}
      {Object.entries(tree.branchErrors).map(([key, message]) => <div key={key} role="alert">分支读取失败：{message}<Button onClick={() => tree.retry({ kind: 'branch', nodeKey: key })}>重试分支</Button></div>)}
      <ConfigProvider theme={{ components: { Tree: { titleHeight: token.controlHeight, nodeSelectedBg: token.colorPrimaryBg, nodeSelectedColor: token.colorPrimaryText } } }}>
      <Tree blockNode showLine={{ showLeafIcon: false }} switcherIcon={<DownOutlined aria-hidden="true" />} treeData={data(roots)} selectedKeys={selected ? [selected.nodeKey] : []} expandedKeys={[...expanded]}
        onExpand={keys => tree.expand(keys.map(String))}
        onSelect={keys => { if (keys.length) tree.select(String(keys[0])); }}
        loadData={node => tree.loadBranch(String(node.key))} />
      </ConfigProvider>
      {!roots.length && !tree.error && <p>暂无节点</p>}
      </Card>
    </aside>
    <section aria-label="节点内容" className="np-tree-content">
      {selected && <header className="np-tree-context-header">
        <h2>{selected.label}</h2>
        {selectedType && <span>{t(`${view.dicKey}.${selectedType.titleKey}`, selectedType.titleKey)}</span>}
      </header>}
      <div className="np-tree-content-body">
      {selected && selectedType ? <PageOutlet key={`${selectedType.key}:${tree.contentEpoch}`} page={selectedType.pageRef} featurePlacement={features.placement ? { ...features.placement, staticScope: selectedType.key } : undefined} context={selected} navigation={navigation ? { tabKey: navigation.nodeTab, selectTab: key => runtime.leave(() => navigation.locate(selected.nodeKey, key)) } : null} /> : <p>请选择节点</p>}
      {editPage && <PageOutlet key={`editor:${editContext?.nodeKey ?? "root"}:${String(editPage.parameters?.actionKey ?? editPage.parameters?.standardAction)}`} page={editPage} featurePlacement={editFeature} context={editContext} close={() => setEditPage(null)} />}
      </div>
    </section>
  </div></TreeInstanceContext.Provider>;
}
