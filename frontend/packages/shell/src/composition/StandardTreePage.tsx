import { useCallback, useContext, useEffect, useId, useMemo, useRef, useState } from 'react';
import Tree from 'antd/es/tree';
import type { DataNode } from 'antd/es/tree';
import Dropdown from 'antd/es/dropdown';
import Button from 'antd/es/button';
import { useLocalization } from '@icp-platform/core';
import { ModuleRegistry, type PageProps, type PageRef, type ResourceChange, type TreeNodeDescriptor, type TreeView } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { CompositionContext } from './pageCompositionRuntime';
import { PageOutlet } from './PageOutlet';

/** 公共树管理页面只消费 Provider 节点与 PageRef，不解释业务标识或组织字段。 */
export default function StandardTreePage({ input }: PageProps) {
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
  const [editPage, setEditPage] = useState<PageRef | null>(null);
  const selectedRef = useRef(selected); selectedRef.current = selected;
  const rootsRef = useRef(roots); rootsRef.current = roots;
  const branchesRef = useRef(branches); branchesRef.current = branches;
  const reloadRoots = useCallback(async (signal?: AbortSignal) => {
    try { const nodes = await provider.roots(treeScope, signal); if (signal?.aborted) return; setRoots(nodes); setSelected(current => current ?? nodes[0] ?? null); setError(null); }
    catch (failure) { if (!signal?.aborted) setError(String(failure)); }
  }, [provider, treeScope]);
  useEffect(() => { const controller = new AbortController(); void reloadRoots(controller.signal); return () => controller.abort(); }, [reloadRoots]);
  const synchronize = useCallback(async (change: ResourceChange) => {
    const impact = await provider.resolveChange(change, treeScope);
    if (impact.kind === 'unrelated') return;
    const parents = impact.kind === 'located'
      ? [...new Set([impact.beforeLocation?.parentNodeKey, impact.afterLocation?.parentNodeKey].filter(key => key !== undefined))]
      : [null, ...Object.keys(branchesRef.current)];
    const refreshed = { ...branchesRef.current };
    let currentRoots = rootsRef.current;
    for (const parent of parents) {
      if (parent === null) { currentRoots = await provider.roots(treeScope); setRoots(currentRoots); }
      else if (parent !== undefined) refreshed[parent] = await provider.children(treeScope, parent);
    }
    const target = impact.kind === 'located' && impact.operation === 'create' ? impact.nodeKey : selectedRef.current?.nodeKey;
    if (target) {
      const path = await provider.path(treeScope, target);
      for (const ancestor of path.slice(0, -1)) {
        if (!parents.includes(ancestor.nodeKey)) refreshed[ancestor.nodeKey] = await provider.children(treeScope, ancestor.nodeKey);
      }
      setExpanded(current => [...new Set([...current, ...path.slice(0, -1).map(node => node.nodeKey)])]);
      const allNodes = [...currentRoots, ...Object.values(refreshed).flat()];
      const resolved = allNodes.find(node => node.nodeKey === target);
      if (!resolved) throw new Error('路径目标未出现在当前分支');
      setSelected(resolved);
    }
    setBranches(refreshed);
    setEditPage(null);
  }, [provider, treeScope]);
  useEffect(() => runtime.subscribe(instanceId, synchronize), [instanceId, runtime, synchronize]);
  const select = (node: TreeNodeDescriptor, after?: () => void) => runtime.leave(() => { setSelected(node); setEditPage(null); after?.(); });
  const data = (nodes: TreeNodeDescriptor[]): DataNode[] => nodes.map(node => {
    const type = view.nodeTypes.find(item => item.key === node.nodeType);
    const title = <span title={type ? t(`${view.dicKey}.${type.titleKey}`, type.titleKey) : ''}>{node.label}{node.isEnabled === false ? '（停用）' : ''}</span>;
    return { key: node.nodeKey, isLeaf: !node.hasChildren,
      title: <Dropdown trigger={['contextMenu']} open={menuNode === node.nodeKey} menu={{ items: (type?.contextMenu ?? []).map(menu => ({ key: menu.key, label: t(`${view.dicKey}.${menu.labelKey}`, menu.labelKey), onClick: () => select(node, () => setEditPage({ pageKey: 'platform.list', parameters: { viewKey: menu.viewKey, presentation: 'drawer' }, inputResolverKey: menu.inputResolverKey })) })) }}
        onOpenChange={open => { if (open) select(node, () => setMenuNode(node.nodeKey)); else setMenuNode(null); }}>{title}</Dropdown>,
      children: branches[node.nodeKey] ? data(branches[node.nodeKey]) : undefined };
  });
  const descriptors = [...roots, ...Object.values(branches).flat()];
  const selectedType = view.nodeTypes.find(type => type.key === selected?.nodeType);
  return <div style={{ display: 'grid', gridTemplateColumns: 'minmax(220px, 28%) 1fr', gap: 24 }}>
    <aside aria-label="结构树">
      {error && <div role="alert">树读取失败：{error}<Button onClick={() => void reloadRoots()}>重试</Button></div>}
      <Tree treeData={data(roots)} selectedKeys={selected ? [selected.nodeKey] : []} expandedKeys={expanded}
        onExpand={keys => setExpanded(keys.map(String))}
        onSelect={keys => { const node = descriptors.find(item => item.nodeKey === keys[0]); if (node) select(node); }}
        loadData={async node => { if (!branches[String(node.key)]) { const children = await provider.children(treeScope, String(node.key)); setBranches(current => ({ ...current, [String(node.key)]: children })); } }} />
      {!roots.length && !error && <p>暂无节点</p>}
    </aside>
    <section aria-label="节点内容">
      {selected && selectedType ? <PageOutlet key={selectedType.key} page={selectedType.pageRef} context={selected} /> : <p>请选择节点</p>}
      {editPage && selected && <PageOutlet key={`editor:${selected.nodeKey}`} page={editPage} context={selected} close={() => setEditPage(null)} />}
    </section>
  </div>;
}
