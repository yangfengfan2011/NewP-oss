/** 将当前页面范围转换为完整出现位置，保留原输入错误和 JSON 解析结果。 */
export function workspacePosition(scope?: Record<string, unknown>) {
  if (typeof scope?.spaceId !== 'string' || typeof scope.rootFolderId !== 'string' || typeof scope.linkIds !== 'string') throw new Error('缺少工作空间出现位置');
  const linkIds: unknown = JSON.parse(scope.linkIds);
  if (!Array.isArray(linkIds) || linkIds.some(id => typeof id !== 'string')) throw new Error('无效工作空间出现位置');
  return { spaceId: scope.spaceId, rootFolderId: scope.rootFolderId, linkIds: linkIds as string[] };
}
