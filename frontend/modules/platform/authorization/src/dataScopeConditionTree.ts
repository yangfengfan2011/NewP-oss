import type { DataScopeCondition } from './DataScopeConditionEditor';

export const conditionGroup = (operator: 'and' | 'or', children: DataScopeCondition[]): DataScopeCondition =>
  ({ type: 'group', field: '', operator, value: null, children });

/** 旧单条件只在视图中包裹；实际编辑前不写回，也不把页面标为已修改。 */
export const asConditionGroup = (condition: DataScopeCondition): DataScopeCondition =>
  condition.type === 'group' ? condition : conditionGroup('and', [condition]);

export const parentConditionPath = (path: string): string => path.slice(0, path.lastIndexOf('.'));
const childIndex = (path: string) => Number(path.slice(path.lastIndexOf('.') + 1)) - 1;
const indices = (path: string) => {
  if (!/^1(?:\.[1-9]\d*)*$/.test(path)) throw new Error('条件路径无效。');
  return path.split('.').slice(1).map(value => Number(value) - 1);
};
export function conditionAt(root: DataScopeCondition, path: string): DataScopeCondition {
  let node = asConditionGroup(root);
  for (const index of indices(path)) {
    if (!node.children?.[index]) throw new Error('条件已不存在。');
    node = node.children[index];
  }
  return node;
}
export function replaceCondition(root: DataScopeCondition, path: string, change: (node: DataScopeCondition) => DataScopeCondition): DataScopeCondition {
  const positions = indices(path);
  conditionAt(root, path);
  const visit = (node: DataScopeCondition, depth: number): DataScopeCondition => depth === positions.length ? change(node)
    : { ...node, children: node.children!.map((child, index) => index === positions[depth] ? visit(child, depth + 1) : child) };
  return visit(asConditionGroup(root), 0);
}

/** 多选只接受同一父组，按原顺序合并，在第一个选中位置插入新组。 */
export function groupConditions(root: DataScopeCondition, paths: string[], operator: 'and' | 'or'): DataScopeCondition {
  const unique = [...new Set(paths)];
  const parent = parentConditionPath(unique[0] ?? '');
  if (unique.length < 2 || !parent || unique.some(path => parentConditionPath(path) !== parent)) throw new Error('请选择同一层的至少两个条件。');
  for (const path of unique) conditionAt(root, path);
  const selected = new Set(unique.map(childIndex));
  const first = [...selected].reduce((minimum, index) => Math.min(minimum, index), Infinity);
  return replaceCondition(root, parent, node => ({ ...node, children: node.children!.flatMap((child, index) => index === first
    ? [conditionGroup(operator, node.children!.filter((_, index) => selected.has(index)))] : selected.has(index) ? [] : [child]) }));
}

/** 解除只移除分组容器；是否改变逻辑由界面比较父子操作符并展示前后预览。 */
export function ungroupCondition(root: DataScopeCondition, path: string): DataScopeCondition {
  const node = conditionAt(root, path);
  const parent = parentConditionPath(path);
  if (!parent || node.type !== 'group') throw new Error('只能解除内部条件组。');
  if (!node.children?.length) return removeCondition(root, path);
  return replaceCondition(root, parent, parent => ({ ...parent,
    children: parent.children!.flatMap((child, index) => index === childIndex(path) ? node.children ?? [] : [child]) }));
}

/** 提升到父组的同层；被移空的父组一并移除，避免留下无法保存的空子组。 */
export function moveConditionOut(root: DataScopeCondition, path: string): DataScopeCondition {
  const parent = parentConditionPath(path);
  const grandparent = parentConditionPath(parent);
  if (!grandparent) throw new Error('已在最外层。');
  const node = conditionAt(root, path);
  return replaceCondition(root, grandparent, group => ({ ...group, children: group.children!.flatMap((child, index) => {
    if (index !== childIndex(parent)) return [child];
    const remaining = child.children!.filter((_, index) => index !== childIndex(path));
    return remaining.length ? [{ ...child, children: remaining }, node] : [node];
  }) }));
}

/** 删除指定分支，只向上清理此次删除造成的空组；整体空组保留为待填写草稿。 */
export function removeCondition(root: DataScopeCondition, path: string): DataScopeCondition {
  const positions = indices(path);
  if (!positions.length) throw new Error('整体组合不能删除。');
  conditionAt(root, path);
  const visit = (node: DataScopeCondition, depth: number): DataScopeCondition | undefined => {
    if (depth === positions.length) return undefined;
    const children = node.children!.flatMap((child, index) => {
      const next = index === positions[depth] ? visit(child, depth + 1) : child;
      return next ? [next] : [];
    });
    return depth > 0 && children.length === 0 ? undefined : { ...node, children };
  };
  return visit(asConditionGroup(root), 0)!;
}
