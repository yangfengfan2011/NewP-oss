import { StandardCrudRequestError } from '@icp-platform/sdk';
interface NamedNode { id: string; name: string; children: NamedNode[] }

export function nextRuleName(base: string, siblings: Pick<NamedNode, 'name'>[]): string {
  const names = new Set(siblings.map(node => node.name.trim()));
  let name = base;
  for (let suffix = 2; names.has(name); suffix++) name = `${base}${suffix}`;
  return name;
}

/** 根和每个父节点的直接子项分别判重，避免把跨分支同名误报为冲突。 */
export function validateRuleNames(root: NamedNode, roots: NamedNode[]): void {
  const visit = (nodes: NamedNode[]) => {
    const names = new Set<string>();
    for (const node of nodes) {
      const name = node.name.trim();
      if (!name || name.length > 256) throw new StandardCrudRequestError('规则名称不能为空，且不能超过256个字符。', 400, {}, { reasonKey: 'AuthorizationErrors.DATA_SCOPE_RULE_NAME_INVALID' });
      if (names.has(name)) throw new StandardCrudRequestError(`同级规则名称“${name}”已存在。`, 400, {}, { reasonKey: 'AuthorizationErrors.DATA_SCOPE_RULE_NAME_DUPLICATE', args: { name: name } });
      names.add(name);
    }
  };
  visit([root]);
  if (roots.some(node => node.id !== root.id && node.name.trim() === root.name.trim()))
    throw new StandardCrudRequestError(`同级规则名称“${root.name.trim()}”已存在。`, 400, {}, { reasonKey: 'AuthorizationErrors.DATA_SCOPE_RULE_NAME_DUPLICATE', args: { name: root.name.trim() } });
  const children = (node: NamedNode) => { visit(node.children); node.children.forEach(children); };
  children(root);
}

export function trimRuleNames<T extends NamedNode>(node: T): T {
  return { ...node, name: node.name.trim(), children: node.children.map(trimRuleNames) };
}
