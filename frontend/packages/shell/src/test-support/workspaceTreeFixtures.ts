import type { TreeNodeDescriptor } from '@icp-platform/sdk';

export function workspaceTreePosition(nodeKey: string) {
  const [spaceId, rootFolderId, ...linkIds] = nodeKey.split(':').map(decodeURIComponent);
  return { spaceId, rootFolderId, linkIds };
}

export function workspaceTreeRoots(spaces: Array<{ spaceId: string; rootFolderId: string; name: string; spaceKind: string; hasChildren?: boolean }>): TreeNodeDescriptor[] {
  return spaces.map(space => ({ nodeKey: `${space.spaceId}:${space.rootFolderId}`, nodeType: 'folder', label: space.name, labelKey: `Workspace.${space.spaceKind}`,
    hasChildren: space.hasChildren ?? false, contextMenuKeys: ['paste', 'clearClipboard'], context: { spaceId: space.spaceId, rootFolderId: space.rootFolderId, id: space.rootFolderId, linkIds: [] } }));
}

export function workspaceTreePath(nodeKey: string, folderIds: string[], names: string[]): TreeNodeDescriptor[] {
  const position = workspaceTreePosition(nodeKey);
  return names.map((label, index) => ({ nodeKey: [position.spaceId, position.rootFolderId, ...position.linkIds.slice(0, index)].map(encodeURIComponent).join(':'),
    nodeType: 'folder', label, hasChildren: index < names.length - 1, context: { ...position, id: folderIds[index], linkIds: position.linkIds.slice(0, index) } }));
}

export function workspaceTreeChildren(nodeKey: string, rows: Array<{ linkId: string; associatedObject: { id: string }; fields: { Name: string }; hasChildren: boolean }>): TreeNodeDescriptor[] {
  const position = workspaceTreePosition(nodeKey);
  return rows.map(row => ({ nodeKey: `${nodeKey}:${encodeURIComponent(row.linkId)}`, nodeType: 'folder', label: row.fields.Name, hasChildren: row.hasChildren,
    context: { ...position, id: row.associatedObject.id, linkIds: [...position.linkIds, row.linkId] } }));
}
