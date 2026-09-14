export interface FeaturePoint {
  position: string; parentPosition: string | null; moduleId: string; key: string;
  kind: string; title: string; behaviorKey: string; entryDependencies: string[]; order: number;
}
export interface FeatureTreeNode { key: string; title: string; children?: FeatureTreeNode[] }

// 搜索与授权筛选只改变可见节点，完整勾选集合始终由页面草稿持有。
export function buildFeatureTree(points: FeaturePoint[], query: string, selected: Set<string>, grantedOnly: boolean): FeatureTreeNode[] {
  const byKey = new Map(points.map(point => [point.position, point]));
  const visible = new Set<string>();
  const needle = query.trim().toLocaleLowerCase();
  for (const point of points) {
    if ((grantedOnly && !selected.has(point.position)) || (needle && !`${point.title} ${point.position}`.toLocaleLowerCase().includes(needle))) continue;
    let current: FeaturePoint | undefined = point;
    while (current && !visible.has(current.position)) {
      visible.add(current.position);
      current = current.parentPosition ? byKey.get(current.parentPosition) : undefined;
    }
  }
  const nodes = new Map(points.filter(point => visible.has(point.position)).map(point => [point.position, { key: point.position, title: point.title, children: [] } as FeatureTreeNode]));
  const roots: FeatureTreeNode[] = [];
  for (const point of [...points].sort((a, b) => a.order - b.order || a.position.localeCompare(b.position))) {
    const node = nodes.get(point.position);
    if (!node) continue;
    const parent = point.parentPosition ? nodes.get(point.parentPosition) : undefined;
    if (parent) parent.children!.push(node); else roots.push(node);
  }
  return roots;
}

// 分支批量操作只覆盖当前快照的真实后代，不记录可授权未来节点的通配规则。
export function changeBranch(points: FeaturePoint[], selected: Set<string>, branch: string, checked: boolean): Set<string> {
  const next = new Set(selected);
  for (const point of points) if (point.position === branch || point.entryDependencies.includes(branch)) {
    if (checked) next.add(point.position); else next.delete(point.position);
  }
  return next;
}

// 展示完整草稿与已保存集合的差异，不受树筛选状态影响。
export function featureDiff(saved: Set<string>, draft: Set<string>) {
  return { added: [...draft].filter(key => !saved.has(key)), removed: [...saved].filter(key => !draft.has(key)) };
}
