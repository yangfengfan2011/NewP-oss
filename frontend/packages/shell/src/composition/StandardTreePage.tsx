import { useCallback, useContext, useEffect, useId, useMemo, useRef, useState } from 'react';
import Tree from 'antd/es/tree';
import type { DataNode } from 'antd/es/tree';
import Dropdown from 'antd/es/dropdown';
import Button from 'antd/es/button';
import { useLocalization } from '@icp-platform/core';
import { ModuleRegistry, type PageProps, type PageRef, type TreeNodeDescriptor, type TreeView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext, TreeInstanceContext, type CompositionChange } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';
import { synchronizeTree, type PendingTreeChange } from './treeSynchronization';

const unknownChangeKey = Symbol('unknown tree change');

/** 公共树管理页面只消费 Provider 节点与 PageRef，不解释业务标识或组织字段。 */
export default function StandardTreePage({ input, active = true }: PageProps) {
  const metadata = useRuntimeMetadata();
  const runtime = useContext(CompositionContext)!;
  const instanceId = useId();
  const { t } = useLocalization();
  const view = metadata?.viewDocuments.find(document => document.view.key === input.parameters.viewKey)?.view as unknown as TreeView;
  const treeScope = input.treeScope!;
  const provider = useMemo(() => ModuleRegistry.getAllModules().flatMap(module => module.treeProviders ?? []).find(item => item.key === view.providerKey)!, [view.providerKey]);
  const [roots, setRoots] = useState<TreeNodeDescriptor[]>([]);
  const [branches, setBranches] = useState<Record<string, TreeNodeDescriptor[]>>({});
  const [selected, setSelected] = useState<TreeNodeDescriptor | null>(null);
  const [expanded, setExpanded] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
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
  const reloadRoots = useCallback(async (signal?: AbortSignal) => {
    try { const nodes = await provider.roots(treeScope, signal); if (signal?.aborted) return; setRoots(nodes); setSelected(current => current ?? nodes[0] ?? null); setError(null); }
    catch (failure) { if (!signal?.aborted) setError(String(failure)); }
  }, [provider, treeScope]);
  useEffect(() => { const controller = new AbortController(); void reloadRoots(controller.signal); return () => controller.abort(); }, [reloadRoots]);
  const flush = useCallback(async () => {
    if (!activeRef.current || !pendingChanges.current.size) return;
    if (flushing.current) return flushing.current;
    flushing.current = (async () => {
      const pending = [...pendingChanges.current.entries()];
      try {
        const next = await synchronizeTree(provider, treeScope, {
          roots: rootsRef.current, branches: branchesRef.current, selected: selectedRef.current, expanded: expandedRef.current,
        }, pending.map(([, change]) => change));
        rootsRef.current = next.roots; branchesRef.current = next.branches; selectedRef.current = next.selected;
        setRoots(next.roots); setBranches(next.branches); setSelected(next.selected); setExpanded(next.expanded);
        if (pending.some(([, change]) => change.selectCreated && change.impact.kind === 'located' && change.impact.operation === 'create')) setSelectionEpoch(value => value + 1);
        pending.forEach(([key, change]) => { if (pendingChanges.current.get(key) === change) pendingChanges.current.delete(key); });
        setError(null);
      } catch (failure) { setError(`已保存，刷新失败：${String(failure)}`); throw failure; }
      finally { flushing.current = null; }
    })();
    return flushing.current;
  }, [provider, treeScope]);
  const synchronize = useCallback(async (change: CompositionChange) => {
    if (!receivedChanges.current.has(change)) {
      receivedChanges.current.add(change);
      let impact;
      try { impact = await provider.resolveChange(change, treeScope); }
      catch (failure) {
        pendingChanges.current.set(unknownChangeKey, { impact: { kind: 'unknown' }, selectCreated: false });
        setError(`已保存，刷新失败：${String(failure)}`); throw failure;
      }
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
  const select = (node: TreeNodeDescriptor, after?: () => void) => runtime.leave(() => { setSelected(node); setEditPage(null); after?.(); });
  const data = (nodes: TreeNodeDescriptor[]): DataNode[] => nodes.map(node => {
    const type = view.nodeTypes.find(item => item.key === node.nodeType);
    const title = <span title={type ? t(`${view.dicKey}.${type.titleKey}`, type.titleKey) : ''}>{node.label}{node.isEnabled === false ? '（停用）' : ''}</span>;
    return { key: node.nodeKey, isLeaf: branches[node.nodeKey] ? branches[node.nodeKey].length === 0 : !node.hasChildren,
      title: <Dropdown trigger={['contextMenu']} open={menuNode === node.nodeKey} menu={{ items: (type?.contextMenu ?? []).map(menu => ({ key: menu.key, label: t(`${view.dicKey}.${menu.labelKey}`, menu.labelKey), onClick: () => select(node, () => { setMenuNode(null); setEditContext(node); setEditPage({ pageKey: 'platform.list', parameters: { viewKey: menu.viewKey, presentation: 'drawer', standardAction: menu.standardAction }, inputResolverKey: menu.inputResolverKey }); }) })) }}
        onOpenChange={open => { if (open) select(node, () => setMenuNode(node.nodeKey)); else setMenuNode(null); }}>{title}</Dropdown>,
      children: branches[node.nodeKey] ? data(branches[node.nodeKey]) : undefined };
  });
  const descriptors = [...roots, ...Object.values(branches).flat()];
  const selectedType = view.nodeTypes.find(type => type.key === selected?.nodeType);
  return <TreeInstanceContext.Provider value={instanceId}><div style={{ display: 'grid', gridTemplateColumns: 'minmax(220px, 28%) 1fr', gap: 24 }}>
    <aside aria-label="结构树">
      {view.createAction && <Button onClick={() => runtime.leave(() => setEditPage({ pageKey: 'platform.list', parameters: { viewKey: view.createAction!.viewKey, presentation: 'drawer', standardAction: 'create' } }))}>{t(`${view.dicKey}.${view.createAction.labelKey}`, view.createAction.labelKey)}</Button>}
      {error && <div role="alert">树读取失败：{error}<Button onClick={() => runtime.leave(() => { if (pendingChanges.current.size) void flush().catch(() => undefined); else void reloadRoots(); })}>重试</Button></div>}
      <Tree treeData={data(roots)} selectedKeys={selected ? [selected.nodeKey] : []} expandedKeys={expanded}
        onExpand={keys => setExpanded(keys.map(String))}
        onSelect={keys => { const node = descriptors.find(item => item.nodeKey === keys[0]); if (node) select(node); }}
        loadData={async node => { if (!branches[String(node.key)]) { const children = await provider.children(treeScope, String(node.key)); setBranches(current => ({ ...current, [String(node.key)]: children })); } }} />
      {!roots.length && !error && <p>暂无节点</p>}
    </aside>
    <section aria-label="节点内容">
      {selected && selectedType ? <PageOutlet key={`${selectedType.key}:${selectionEpoch}`} page={selectedType.pageRef} context={selected} /> : <p>请选择节点</p>}
      {editPage && <PageOutlet key={`editor:${editContext?.nodeKey ?? "root"}:${String(editPage.parameters?.standardAction)}`} page={editPage} context={editContext} close={() => setEditPage(null)} />}
    </section>
  </div></TreeInstanceContext.Provider>;
}
