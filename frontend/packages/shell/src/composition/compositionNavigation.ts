import type { TreeLocation, TreeNavigation } from './pageCompositionRuntime';

/** undefined 是保留指令；存储的位置始终只有 string 或 null。 */
function mergeTreeLocation(current: TreeLocation = { nodeKey: null, nodeTab: null }, update: Partial<TreeLocation> = {}): TreeLocation {
  return {
    nodeKey: update.nodeKey === undefined ? current.nodeKey : update.nodeKey,
    nodeTab: update.nodeTab === undefined ? current.nodeTab : update.nodeTab,
  };
}

/** Router adapter 提供当前 search 和实际写回；这里拥有地址槽及历史交接策略。 */
export function createAddressNavigation(search: string, write: (search: string, replace: boolean) => void) {
  const query = new URLSearchParams(search);
  const location = { nodeKey: query.get('node'), nodeTab: query.get('nodeTab') };
  return {
    navigation: {
      tabKey: query.get('tab'),
      selectTab: (key: string, treeLocation?: TreeLocation) => {
        const next = new URLSearchParams(query);
        next.set('tab', key);
        if (treeLocation) {
          const target = mergeTreeLocation(location, treeLocation);
          if (target.nodeKey) next.set('node', target.nodeKey); else next.delete('node');
          if (target.nodeTab) next.set('nodeTab', target.nodeTab); else next.delete('nodeTab');
        }
        write(next.toString(), false);
      },
    },
    treeNavigation: {
      ...location,
      locate: (nodeKey: string | null, nodeTab?: string | null) => {
        const target = mergeTreeLocation(location, { nodeKey, nodeTab });
        const next = new URLSearchParams(query);
        if (target.nodeKey) next.set('node', target.nodeKey); else next.delete('node');
        // 保留旧地址的真值规则：空字符串指令与 null 清除指令不同。
        if (nodeTab) next.set('nodeTab', target.nodeTab!); else if (nodeTab === null) next.delete('nodeTab');
        if (next.toString() !== query.toString()) write(next.toString(), true);
      },
    } satisfies TreeNavigation,
  };
}

/** 每个 Tab 实例创建一份；URL 只投影本组选中标签，兄弟位置随实例销毁。 */
export function createTabLocationMemory() {
  const locations = new Map<string, TreeLocation>();
  const locationFor = (key: string) => mergeTreeLocation(locations.get(key));
  return {
    forSelection(selected: string, upstream: TreeNavigation | null) {
      if (upstream) locations.set(selected, { nodeKey: upstream.nodeKey, nodeTab: upstream.nodeTab });
      return {
        handoff: (key: string) => upstream ? locationFor(key) : undefined,
        forTab: (key: string): TreeNavigation | null => upstream ? {
          ...locationFor(key),
          locate: (nodeKey, nodeTab) => {
            locations.set(key, mergeTreeLocation(locations.get(key), { nodeKey, nodeTab }));
            // 地址授权由本组选择决定，不能改成整个父链的可见性。
            if (key === selected) upstream.locate(nodeKey, nodeTab);
          },
        } : null,
      };
    },
  };
}
