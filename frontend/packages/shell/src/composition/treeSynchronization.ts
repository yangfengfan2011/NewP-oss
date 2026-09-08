import { TreeNodeNotFoundError, type TreeChange, type TreeNodeDescriptor, type TreeProviderBinding } from '@icp-platform/sdk';
export interface TreeSnapshot {
  roots: TreeNodeDescriptor[]; branches: Record<string, TreeNodeDescriptor[]>;
  selected: TreeNodeDescriptor | null; expanded: string[];
}
export interface PendingTreeChange { impact: TreeChange; selectCreated: boolean; affectedParents?: Array<string | null> }
/** 只从 Provider 位置事实恢复已加载范围；返回完整下一快照，失败不宣称局部同步已完成。 */
export async function synchronizeTree(provider: TreeProviderBinding, scope: Record<string, unknown>, snapshot: TreeSnapshot,
  changes: PendingTreeChange[]): Promise<TreeSnapshot> {
  const parents = new Set<string | null>();
  const removed = new Set<string>();
  let target = snapshot.selected?.nodeKey;
  for (const { impact, selectCreated, affectedParents } of changes) {
    affectedParents?.forEach(parent => parents.add(parent));
    if (impact.kind === 'unrelated') continue;
    if (impact.kind === 'unknown') { parents.add(null); Object.keys(snapshot.branches).forEach(key => parents.add(key)); continue; }
    if (impact.beforeLocation) parents.add(impact.beforeLocation.parentNodeKey);
    if (impact.afterLocation) parents.add(impact.afterLocation.parentNodeKey);
    if (impact.operation === 'create' && selectCreated) target = impact.nodeKey;
    if (impact.operation === 'delete') {
      removed.add(impact.nodeKey);
      if (target === impact.nodeKey) target = impact.beforeLocation?.parentNodeKey ?? undefined;
    }
  }
  let roots = snapshot.roots;
  const branches = { ...snapshot.branches };
  const loaded = new Set<string | null>();
  const read = async (parent: string | null) => {
    if (loaded.has(parent) || (parent !== null && removed.has(parent))) return;
    loaded.add(parent);
    if (parent === null) roots = await provider.roots(scope);
    else {
      try { branches[parent] = await provider.children(scope, parent); }
      catch (error) { if (!(error instanceof TreeNodeNotFoundError)) throw error; removed.add(parent); }
    }
  };
  for (const parent of parents) await read(parent);
  for (const key of removed) delete branches[key];
  // 失效的旧分支副本不能继续暴露移动或已删除节点。
  for (const key of Object.keys(branches)) branches[key] = branches[key].filter(node => !removed.has(node.nodeKey));
  if (!target) target = roots[0]?.nodeKey;
  let path: { nodeKey: string }[] = [];
  if (target) {
    try { path = await provider.path(scope, target); }
    catch (error) { if (!(error instanceof TreeNodeNotFoundError)) throw error; }
    if (!path.length) { target = roots[0]?.nodeKey; path = target ? await provider.path(scope, target) : []; }
    for (const ancestor of path.slice(0, -1)) if (!Object.prototype.hasOwnProperty.call(branches, ancestor.nodeKey)) await read(ancestor.nodeKey);
  }
  const selected = [...roots, ...Object.values(branches).flat()].find(node => node.nodeKey === target) ?? null;
  if (target && !selected) throw new Error('路径目标未出现在当前分支');
  return { roots, branches, selected,
    expanded: [...new Set([...snapshot.expanded.filter(key => !removed.has(key)), ...path.slice(0, -1).map(node => node.nodeKey)])] };
}
