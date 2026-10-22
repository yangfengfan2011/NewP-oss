interface EntryPoint { position: string; parentPosition?: string | null; container?: boolean }

/** 只计算活动目录中的展示资格；不读取子页面或改变用户授权集合。 */
export function createFeatureVisibility(points: readonly EntryPoint[], granted: (position: string) => boolean) {
  const byPosition = new Map(points.map(point => [point.position, point]));
  const children = new Map<string, string[]>();
  for (const point of points) if (point.parentPosition) {
    const branch = children.get(point.parentPosition) ?? [];
    branch.push(point.position);
    children.set(point.parentPosition, branch);
  }
  const computed = new Map<string, boolean>();
  const visible = (position: string): boolean => {
    if (computed.has(position)) return computed.get(position)!;
    const point = byPosition.get(position);
    computed.set(position, false);
    const result = !!point && granted(position) && (!point.container || (children.get(position) ?? []).some(visible));
    computed.set(position, result);
    return result;
  };
  return visible;
}
