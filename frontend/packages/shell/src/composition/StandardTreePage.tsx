import { useFeaturePlacement, type FeaturePlacement } from '../authorization/FeaturePlacementContext';
import { useContext, useEffect, useId, useMemo, useRef, useState } from 'react';
import Tree from 'antd/es/tree';
import type { DataNode } from 'antd/es/tree';
import { ContextActionMenu } from '@icp-platform/ui/core';
import Button from 'antd/es/button';
import Card from 'antd/es/card';
import Breadcrumb from 'antd/es/breadcrumb';
import ConfigProvider from 'antd/es/config-provider';
import Space from 'antd/es/space';
import PlusOutlined from '@ant-design/icons/PlusOutlined';
import DownOutlined from '@ant-design/icons/DownOutlined';
import theme from 'antd/es/theme';
import { formatRequestError, useLocalization } from '@icp-platform/core';
import { ModuleRegistry, type PageProps, type PageRef, type TreeNodeDescriptor, type TreeView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext, TreeInstanceContext, TreeNavigationContext } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';
import { useTreeReadLifecycle } from './useTreeReadLifecycle';
import { TreeSearch } from './TreeSearch';
import { createRelationTreeProvider, relationTreeProviderKey } from './relationTreeProvider';
import { resolvedFeaturePosition } from '../authorization/featurePlacementResolver';
import { useOptionalAuth } from '../contexts/AuthContext';

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
  const subjectId = useOptionalAuth()?.user?.id;
  const [menuNode, setMenuNode] = useState<{ key: string; scope: string; context: string } | null>(null);
  const menuScope = JSON.stringify([input.treeScope, input.parameters, features.placement, active, subjectId]);
  const currentMenuScope = useRef(menuScope); currentMenuScope.current = menuScope;
  useEffect(() => setMenuNode(null), [menuScope]);
  const [editContext, setEditContext] = useState<TreeNodeDescriptor | undefined>();
  const [editPage, setEditPage] = useState<PageRef | null>(null);
  const tree = useTreeReadLifecycle({
    provider, scope: treeScope, instanceId, active, runtime, navigation,
    onSelectAccepted: () => { setEditPage(null); setMenuNode(null); },
  });
  const { roots, branches, selected, expanded } = tree.snapshot;
  const menuExecutable = useRef<{ nodeKey?: string; context?: string; scope: string; references: string[] }>({ scope: menuScope, references: [] });
  menuExecutable.current = { nodeKey: active ? selected?.nodeKey : undefined, context: JSON.stringify(selected), scope: menuScope,
    references: (view.nodeTypes.find(type => type.key === selected?.nodeType)?.contextMenu ?? [])
      .filter(menu => (selected?.contextMenuKeys === undefined || selected.contextMenuKeys.includes(menu.key))
        && (!features.placement || features.action(menu.key, 'contextMenu', selected?.nodeType).kind === 'allowed'))
      .map(menu => JSON.stringify(menu)) };
  // 只沿已加载的出现位置回溯；不解析 opaque nodeKey，也不为面包屑读取未展开的树。
  const selectedPath = useMemo(() => {
    if (!selected) return [];
    const parents = new Map<string, string>();
    const nodes = new Map(roots.map(node => [node.nodeKey, node]));
    for (const [parent, children] of Object.entries(branches)) {
      for (const child of children) { parents.set(child.nodeKey, parent); nodes.set(child.nodeKey, child); }
    }
    const path: TreeNodeDescriptor[] = [];
    const visited = new Set<string>();
    let key: string | undefined = selected.nodeKey;
    while (key && !visited.has(key)) {
      visited.add(key);
      const node = nodes.get(key);
      if (!node) break;
      path.push(node);
      key = parents.get(key);
    }
    return path.reverse();
  }, [roots, branches, selected]);
  const data = (nodes: readonly TreeNodeDescriptor[]): DataNode[] => nodes.map(node => {
    const type = view.nodeTypes.find(item => item.key === node.nodeType);
    const nodeLabel = (node.labelKey ? t(node.labelKey, node.label) : node.label) + (node.isEnabled === false ? t('Platform.TreeDisabledSuffix', '（停用）') : '');
    const title = <span style={{ whiteSpace: 'normal', overflowWrap: 'anywhere' }} title={type ? t(`${view.dicKey}.${type.titleKey}`, type.titleKey) : ''}>{nodeLabel}</span>;
    const menus = (type?.contextMenu ?? []).filter(menu => node.contextMenuKeys === undefined || node.contextMenuKeys.includes(menu.key))
      .map(menu => ({ menu, resolution: features.action(menu.key, 'contextMenu', node.nodeType) }))
      .filter(({ resolution }) => !features.placement || resolution.kind === 'allowed');
    const open = active && menuNode?.key === node.nodeKey && menuNode.scope === menuScope
      && menuNode.context === JSON.stringify(node) && selected?.nodeKey === node.nodeKey;
    return { key: node.nodeKey, 'aria-label': nodeLabel, isLeaf: branches[node.nodeKey] ? branches[node.nodeKey].length === 0 : !node.hasChildren,
      title: menus.length ? <ContextActionMenu trigger={['contextMenu']} open={open}
        keyboardLabel={nodeLabel}
        menu={{ items: menus.map(({ menu, resolution }) => ({ key: menu.key, label: t(`${view.dicKey}.${menu.labelKey}`, menu.labelKey), onClick: () => {
          if (!open || menuExecutable.current.scope !== menuScope || menuExecutable.current.nodeKey !== node.nodeKey
            || menuExecutable.current.context !== JSON.stringify(node) || !menuExecutable.current.references.includes(JSON.stringify(menu))) return;
          tree.select(node.nodeKey, () => {
            if (currentMenuScope.current !== menuScope) return;
            setMenuNode(null); setEditFeature(features.placement ? { ...features.placement, staticScope: node.nodeType, actionPosition: resolvedFeaturePosition(resolution), actionKey: menu.actionKey ?? menu.standardAction } : undefined);
            setEditContext(node); setEditPage({ pageKey: 'platform.list', parameters: { viewKey: menu.viewKey, presentation: 'drawer', standardAction: menu.standardAction, actionKey: menu.actionKey }, inputResolverKey: menu.inputResolverKey });
          });
        } })) }}
        onOpenChange={next => { if (next) tree.select(node.nodeKey, () => {
          if (currentMenuScope.current === menuScope) setMenuNode({ key: node.nodeKey, scope: menuScope, context: JSON.stringify(node) });
        }); else setMenuNode(null); }}>{title}</ContextActionMenu> : title,
      children: branches[node.nodeKey] ? data(branches[node.nodeKey]) : undefined };
  });
  const selectedType = view.nodeTypes.find(type => type.key === selected?.nodeType);
  return <TreeInstanceContext.Provider value={instanceId}><div className="np-tree-workspace">
    <aside aria-label={t('Platform.TreeStructure', '结构树')} style={{ minWidth: 0 }}>
      <Card size="small" className="np-tree-sidebar-card"
        styles={{ header: { minHeight: 'var(--np-workspace-header-height)', padding: `${token.paddingXXS}px ${token.paddingSM}px` }, body: { padding: token.paddingSM } }}
        title={view.treeActions?.length ? <Space role="toolbar" aria-label={t('Platform.TreeActions', '树操作')} size={token.marginXS} style={{ width: '100%' }} wrap>
          {view.treeActions.map(action => ({ action, resolution: features.action(action.key, 'treeActions') })).filter(({ resolution }) => !features.placement || resolution.kind === 'allowed').map(({ action, resolution }) => <Button key={action.key} size="small" icon={action.standardAction === 'create' ? <PlusOutlined aria-hidden="true" /> : undefined}
            onClick={() => runtime.leave(() => { setEditContext(undefined); setEditFeature(features.placement ? { ...features.placement, actionPosition: resolvedFeaturePosition(resolution), actionKey: action.actionKey ?? action.standardAction } : undefined); setEditPage({ pageKey: 'platform.list', parameters: { viewKey: action.viewKey, presentation: 'drawer', standardAction: action.standardAction, actionKey: action.actionKey }, inputResolverKey: action.inputResolverKey }); })}>{t(`${view.dicKey}.${action.labelKey}`, action.labelKey)}</Button>)}
        </Space> : undefined}>
      {view.searchEnabled !== false && <TreeSearch provider={provider} scope={treeScope} view={view} choose={tree.locate} />}
      {tree.error && <div role="alert">{tree.error.saved ? t('Platform.SavedRefreshFailed', '已保存，刷新失败。请重新加载。') : t('Platform.TreeReadFailed', '树读取失败：{message}', { message: formatRequestError(tree.error.failure, t) })}<Button onClick={() => tree.retry({ kind: 'tree' })}>{t('Platform.Retry', '重试')}</Button></div>}
      {tree.locationError != null && <div role="alert">{t('Platform.TreeLocationFailed', '定位读取失败：{message}', { message: formatRequestError(tree.locationError, t) })}<Button onClick={() => tree.retry({ kind: 'location' })}>{t('Platform.TreeRetryLocation', '重试定位')}</Button></div>}
      {tree.locationNotice && <div role="status">{t(tree.locationNotice, '目标节点已不存在')}</div>}
      {Object.entries(tree.branchErrors).map(([key, message]) => <div key={key} role="alert">{t('Platform.TreeBranchFailed', '分支读取失败：{message}', { message: formatRequestError(message, t) })}<Button onClick={() => tree.retry({ kind: 'branch', nodeKey: key })}>{t('Platform.TreeRetryBranch', '重试分支')}</Button></div>)}
      <ConfigProvider theme={{ components: { Tree: { titleHeight: token.controlHeight, nodeSelectedBg: token.colorPrimaryBg, nodeSelectedColor: token.colorPrimaryText } } }}>
      <Tree blockNode showLine={{ showLeafIcon: false }} switcherIcon={<DownOutlined aria-hidden="true" />} treeData={data(roots)} selectedKeys={selected ? [selected.nodeKey] : []} expandedKeys={[...expanded]}
        onExpand={keys => tree.expand(keys.map(String))}
        onSelect={keys => { if (keys.length) tree.select(String(keys[0])); }}
        loadData={node => tree.loadBranch(String(node.key))} />
      </ConfigProvider>
      {!roots.length && !tree.error && <p>{t('Platform.TreeEmpty', '暂无节点')}</p>}
      </Card>
    </aside>
    <section aria-label={t('Platform.TreeContent', '节点内容')} className="np-tree-content">
      {selected && <header className="np-tree-context-header">
        <Breadcrumb aria-label={t('PlatformShell.Breadcrumb', '面包屑')} items={selectedPath.map((node, index) => ({ key: node.nodeKey,
          title: index === selectedPath.length - 1 ? <span>{node.labelKey ? t(node.labelKey, node.label) : node.label}</span>
            : <button type="button" className="np-tree-breadcrumb-link" onClick={() => tree.select(node.nodeKey)}>{node.labelKey ? t(node.labelKey, node.label) : node.label}</button>,
        }))} />
        <h2>{selected.labelKey ? t(selected.labelKey, selected.label) : selected.label}</h2>
        {selectedType && <span>{t(`${view.dicKey}.${selectedType.titleKey}`, selectedType.titleKey)}</span>}
      </header>}
      <div className="np-tree-content-body">
      {selected && selectedType ? <PageOutlet key={`${selectedType.key}:${tree.contentEpoch}`} page={selectedType.pageRef} featurePlacement={features.placement ? { ...features.placement, staticScope: selectedType.key } : undefined} context={selected} navigation={navigation ? { tabKey: navigation.nodeTab, selectTab: key => runtime.leave(() => navigation.locate(selected.nodeKey, key)) } : null} /> : <p>{t('Platform.TreeSelectNode', '请选择节点')}</p>}
      {editPage && <PageOutlet key={`editor:${editContext?.nodeKey ?? "root"}:${String(editPage.parameters?.actionKey ?? editPage.parameters?.standardAction)}`} page={editPage} featurePlacement={editFeature} context={editContext} close={() => setEditPage(null)} />}
      </div>
    </section>
  </div></TreeInstanceContext.Provider>;
}
