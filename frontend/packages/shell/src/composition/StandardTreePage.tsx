import { useCallback, useContext, useEffect, useId, useMemo, useRef, useState } from 'react';
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
import { ModuleRegistry, TreeNodeNotFoundError, type PageProps, type PageRef, type TreeNodeDescriptor, type TreeView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext, TreeInstanceContext, TreeNavigationContext, type CompositionChange } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';
import { synchronizeTree, type PendingTreeChange } from './treeSynchronization';
import { TreeSearch } from './TreeSearch';

const unknownChangeKey = Symbol('unknown tree change');

/** 公共树管理页面只消费 Provider 节点与 PageRef，不解释业务标识或组织字段。 */
export default function StandardTreePage({ input, active = true }: PageProps) {
  const metadata = useRuntimeMetadata();
  const runtime = useContext(CompositionContext)!;
  const navigation = useContext(TreeNavigationContext);
  const navigationRef = useRef(navigation); navigationRef.current = navigation;
  const instanceId = useId();
  const { t } = useLocalization();
  const { token } = theme.useToken();
  const view = metadata?.viewDocuments.find(document => document.view.key === input.parameters.viewKey)?.view as unknown as TreeView;
  const treeScope = input.treeScope!;
  const provider = useMemo(() => ModuleRegistry.getAllModules().flatMap(module => module.treeProviders ?? []).find(item => item.key === view.providerKey)!, [view.providerKey]);
  const [roots, setRoots] = useState<TreeNodeDescriptor[]>([]);
  const [branches, setBranches] = useState<Record<string, TreeNodeDescriptor[]>>({});
  const [selected, setSelected] = useState<TreeNodeDescriptor | null>(null);
  const [expanded, setExpanded] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [locationError, setLocationError] = useState<string | null>(null);
  const [locationNotice, setLocationNotice] = useState<string | null>(null);
  const [branchErrors, setBranchErrors] = useState<Record<string, string>>({});
  const scopeController = useRef(new AbortController());
  const rootsController = useRef<AbortController>();
  const branchControllers = useRef(new Map<string, AbortController>());
  const locationController = useRef<AbortController>();
  const locationTarget = useRef<string>();
  const [menuNode, setMenuNode] = useState<string | null>(null);
  const [editContext, setEditContext] = useState<TreeNodeDescriptor | undefined>();
  const [editPage, setEditPage] = useState<PageRef | null>(null);
  const [selectionEpoch, setSelectionEpoch] = useState(0);
  const pendingChanges = useRef(new Map<string | symbol, PendingTreeChange>());
  const receivedChanges = useRef(new WeakSet<CompositionChange>());
  const activeRef = useRef(active); activeRef.current = active;
  const expandedRef = useRef(expanded); expandedRef.current = expanded;
  const flushing = useRef<Promise<void> | null>(null);
  const selectedRef = useRef(selected); selectedRef.current = selected;
  const rootsRef = useRef(roots); rootsRef.current = roots;
  const branchesRef = useRef(branches); branchesRef.current = branches;
  const locate = useCallback(async (key: string, currentRoots = rootsRef.current) => {
    locationTarget.current = key;
    locationController.current?.abort();
    const controller = new AbortController(); locationController.current = controller;
    const signal = controller.signal;
    try {
      const path = await provider.path(treeScope, key, signal);
      if (!path.length || path[path.length - 1]?.nodeKey !== key) throw new Error('路径未包含目标');
      const loaded: Record<string, TreeNodeDescriptor[]> = {};
      for (const ancestor of path.slice(0, -1)) {
        if (signal.aborted) return;
        loaded[ancestor.nodeKey] = await provider.children(treeScope, ancestor.nodeKey, signal);
      }
      if (signal.aborted) return;
      const target = [...currentRoots, ...Object.values(loaded).flat()].find(node => node.nodeKey === key);
      if (!target) throw new Error('路径目标未出现在当前分支');
      // 读取期间旧页面仍可编辑；落地前再次裁决，确认回调也不得应用已取消的定位。
      runtime.leave(() => {
        if (signal.aborted || !activeRef.current) return;
        setBranches(current => ({ ...current, ...loaded })); setExpanded(current => [...new Set([...current, ...path.slice(0, -1).map(node => node.nodeKey)])]);
        setSelected(target); setEditPage(null); setLocationError(null); setLocationNotice(null);
        navigationRef.current?.locate(key, selectedRef.current && selectedRef.current.nodeType !== target.nodeType ? null : undefined);
      });
    } catch (failure) {
      if (signal.aborted) return;
      if (failure instanceof TreeNodeNotFoundError) {
        setLocationNotice('目标节点已不存在');
        runtime.leave(() => { if (signal.aborted || !activeRef.current) return; setSelected(currentRoots[0] ?? null); setLocationError(null); navigationRef.current?.locate(currentRoots[0]?.nodeKey ?? null, null); });
      }
      else setLocationError(String(failure));
    }
  }, [provider, runtime, treeScope]);
  const reloadRoots = useCallback(async (signal?: AbortSignal) => {
    rootsController.current?.abort();
    const controller = new AbortController(); rootsController.current = controller;
    try { const nodes = await provider.roots(treeScope, controller.signal); if (signal?.aborted || controller.signal.aborted) return; setRoots(nodes); rootsRef.current = nodes; setError(null);
      const target = navigationRef.current?.nodeKey;
      if (target) await locate(target, nodes); else setSelected(current => current ?? nodes[0] ?? null);
    }
    catch (failure) { if (!signal?.aborted && !controller.signal.aborted) setError(String(failure)); }
  }, [locate, provider, treeScope]);
  useEffect(() => {
    const controller = new AbortController(); scopeController.current = controller;
    setRoots([]); setBranches({}); setSelected(null); setExpanded([]); setBranchErrors({});
    pendingChanges.current.clear(); receivedChanges.current = new WeakSet();
    void reloadRoots(controller.signal);
    const branchesInScope = branchControllers.current;
    return () => { controller.abort(); rootsController.current?.abort(); locationController.current?.abort(); branchesInScope.forEach(request => request.abort()); };
  }, [reloadRoots]);
  useEffect(() => {
    const currentNavigation = navigationRef.current;
    if (!active || !rootsRef.current.length || !currentNavigation) return;
    if (currentNavigation.nodeKey && currentNavigation.nodeKey !== selectedRef.current?.nodeKey) void locate(currentNavigation.nodeKey);
    else if (!currentNavigation.nodeKey) setSelected(rootsRef.current[0] ?? null);
  }, [active, locate, navigation?.nodeKey]);
  const flush = useCallback(async () => {
    if (!activeRef.current || !pendingChanges.current.size) return;
    if (flushing.current) return flushing.current;
    flushing.current = (async () => {
      const scopeSignal = scopeController.current.signal;
      locationController.current?.abort(); branchControllers.current.forEach(request => request.abort());
      const pending = [...pendingChanges.current.entries()];
      try {
        const next = await synchronizeTree(provider, treeScope, {
          roots: rootsRef.current, branches: branchesRef.current, selected: selectedRef.current, expanded: expandedRef.current,
        }, pending.map(([, change]) => change));
        if (scopeSignal.aborted) return;
        rootsRef.current = next.roots; branchesRef.current = next.branches; selectedRef.current = next.selected;
        setRoots(next.roots); setBranches(next.branches); setSelected(next.selected); setExpanded(next.expanded);
        navigationRef.current?.locate(next.selected?.nodeKey ?? null, pending.some(([, change]) => change.selectCreated && change.impact.kind === 'located' && change.impact.operation === 'create') ? null : undefined);
        if (pending.some(([, change]) => change.selectCreated && change.impact.kind === 'located' && change.impact.operation === 'create')) setSelectionEpoch(value => value + 1);
        pending.forEach(([key, change]) => { if (pendingChanges.current.get(key) === change) pendingChanges.current.delete(key); });
        setError(null);
      } catch (failure) { if (!scopeSignal.aborted) { setError(`已保存，刷新失败：${String(failure)}`); throw failure; } }
      finally { flushing.current = null; }
    })();
    return flushing.current;
  }, [provider, treeScope]);
  const synchronize = useCallback(async (change: CompositionChange) => {
    const scopeSignal = scopeController.current.signal;
    if (!receivedChanges.current.has(change)) {
      receivedChanges.current.add(change);
      let impact;
      try { impact = await provider.resolveChange(change, treeScope); }
      catch (failure) {
        if (scopeSignal.aborted) return;
        pendingChanges.current.set(unknownChangeKey, { impact: { kind: 'unknown' }, selectCreated: false });
        setError(`已保存，刷新失败：${String(failure)}`); throw failure;
      }
      if (scopeSignal.aborted) return;
      if (impact.kind === 'unrelated') return;
      const key = impact.kind === 'located' ? impact.nodeKey : unknownChangeKey;
      const previous = pendingChanges.current.get(key);
      const affectedParents = new Set(previous?.affectedParents ?? []);
      if (impact.kind === 'located') {
        if (impact.beforeLocation) affectedParents.add(impact.beforeLocation.parentNodeKey);
        if (impact.afterLocation) affectedParents.add(impact.afterLocation.parentNodeKey);
      }
      pendingChanges.current.set(key, { impact, affectedParents: [...affectedParents], selectCreated: change.originTreeId === instanceId });
      if (impact.kind === 'unknown') { setError('已保存，刷新失败：影响范围待确认。'); throw new Error('树影响范围待确认'); }
    }
    await flush();
  }, [flush, instanceId, provider, treeScope]);
  useEffect(() => runtime.subscribe(instanceId, synchronize), [instanceId, runtime, synchronize]);
  useEffect(() => { if (active) void flush().catch(() => undefined); }, [active, flush]);
  const select = (node: TreeNodeDescriptor, after?: () => void) => runtime.leave(() => { locationController.current?.abort(); setSelected(node); setEditPage(null); setLocationNotice(null); navigationRef.current?.locate(node.nodeKey, selectedRef.current?.nodeType !== node.nodeType ? null : undefined); after?.(); });
  const readBranch = async (key: string) => {
    branchControllers.current.get(key)?.abort();
    const controller = new AbortController(); branchControllers.current.set(key, controller);
    const signal = controller.signal;
    try { const children = await provider.children(treeScope, key, signal); if (signal.aborted) return;
      setBranches(current => ({ ...current, [key]: children })); setBranchErrors(current => { const next = { ...current }; delete next[key]; return next; });
    } catch (failure) { if (!signal.aborted) setBranchErrors(current => ({ ...current, [key]: String(failure) })); }
  };
  const data = (nodes: TreeNodeDescriptor[]): DataNode[] => nodes.map(node => {
    const type = view.nodeTypes.find(item => item.key === node.nodeType);
    const title = <span style={{ whiteSpace: 'normal', overflowWrap: 'anywhere' }} title={type ? t(`${view.dicKey}.${type.titleKey}`, type.titleKey) : ''}>{node.label}{node.isEnabled === false ? '（停用）' : ''}</span>;
    return { key: node.nodeKey, isLeaf: branches[node.nodeKey] ? branches[node.nodeKey].length === 0 : !node.hasChildren,
      title: <Dropdown trigger={['contextMenu']} open={menuNode === node.nodeKey} menu={{ items: (type?.contextMenu ?? []).map(menu => ({ key: menu.key, label: t(`${view.dicKey}.${menu.labelKey}`, menu.labelKey), onClick: () => select(node, () => { setMenuNode(null); setEditContext(node); setEditPage({ pageKey: 'platform.list', parameters: { viewKey: menu.viewKey, presentation: 'drawer', standardAction: menu.standardAction, actionKey: menu.actionKey }, inputResolverKey: menu.inputResolverKey }); }) })) }}
        onOpenChange={open => { if (open) select(node, () => setMenuNode(node.nodeKey)); else setMenuNode(null); }}>{title}</Dropdown>,
      children: branches[node.nodeKey] ? data(branches[node.nodeKey]) : undefined };
  });
  const descriptors = [...roots, ...Object.values(branches).flat()];
  const selectedType = view.nodeTypes.find(type => type.key === selected?.nodeType);
  return <TreeInstanceContext.Provider value={instanceId}><div className="np-tree-workspace">
    <aside aria-label="结构树" style={{ minWidth: 0 }}>
      <Card size="small" className="np-tree-sidebar-card"
        styles={{ header: { minHeight: token.controlHeightLG, padding: `${token.paddingXXS}px ${token.paddingSM}px` }, body: { padding: token.paddingSM } }}
        title={view.treeActions?.length ? <Space role="toolbar" aria-label="树操作" size={token.marginXS} style={{ width: '100%' }} wrap>
          {view.treeActions.map(action => <Button key={action.key} size="small" icon={action.standardAction === 'create' ? <PlusOutlined aria-hidden="true" /> : undefined}
            onClick={() => runtime.leave(() => { setEditContext(undefined); setEditPage({ pageKey: 'platform.list', parameters: { viewKey: action.viewKey, presentation: 'drawer', standardAction: action.standardAction, actionKey: action.actionKey }, inputResolverKey: action.inputResolverKey }); })}>{t(`${view.dicKey}.${action.labelKey}`, action.labelKey)}</Button>)}
        </Space> : undefined}>
      <TreeSearch provider={provider} scope={treeScope} view={view} choose={key => runtime.leave(() => { void locate(key); })} />
      {error && <div role="alert">树读取失败：{error}<Button onClick={() => runtime.leave(() => { if (pendingChanges.current.size) void flush().catch(() => undefined); else void reloadRoots(); })}>重试</Button></div>}
      {locationError && <div role="alert">定位读取失败：{locationError}<Button onClick={() => runtime.leave(() => { if (locationTarget.current) void locate(locationTarget.current); })}>重试定位</Button></div>}
      {locationNotice && <div role="status">{locationNotice}</div>}
      {Object.entries(branchErrors).map(([key, message]) => <div key={key} role="alert">分支读取失败：{message}<Button onClick={() => void readBranch(key)}>重试分支</Button></div>)}
      <ConfigProvider theme={{ components: { Tree: { titleHeight: token.controlHeight, nodeSelectedBg: token.colorPrimaryBg, nodeSelectedColor: token.colorPrimaryText } } }}>
      <Tree blockNode showLine={{ showLeafIcon: false }} switcherIcon={<DownOutlined aria-hidden="true" />} treeData={data(roots)} selectedKeys={selected ? [selected.nodeKey] : []} expandedKeys={expanded}
        onExpand={keys => setExpanded(keys.map(String))}
        onSelect={keys => { const node = descriptors.find(item => item.nodeKey === keys[0]); if (node) select(node); }}
        loadData={async node => { if (!branches[String(node.key)]) await readBranch(String(node.key)); }} />
      </ConfigProvider>
      {!roots.length && !error && <p>暂无节点</p>}
      </Card>
    </aside>
    <section aria-label="节点内容" className="np-tree-content">
      {selected && <header className="np-tree-context-header">
        <h2>{selected.label}</h2>
        {selectedType && <span>{t(`${view.dicKey}.${selectedType.titleKey}`, selectedType.titleKey)}</span>}
      </header>}
      <div className="np-tree-content-body">
      {selected && selectedType ? <PageOutlet key={`${selectedType.key}:${selectionEpoch}`} page={selectedType.pageRef} context={selected} navigation={navigation ? { tabKey: navigation.nodeTab, selectTab: key => runtime.leave(() => navigation.locate(selected.nodeKey, key)) } : null} /> : <p>请选择节点</p>}
      {editPage && <PageOutlet key={`editor:${editContext?.nodeKey ?? "root"}:${String(editPage.parameters?.actionKey ?? editPage.parameters?.standardAction)}`} page={editPage} context={editContext} close={() => setEditPage(null)} />}
      </div>
    </section>
  </div></TreeInstanceContext.Provider>;
}
