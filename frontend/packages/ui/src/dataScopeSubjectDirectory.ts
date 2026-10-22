export type DataScopeSubjectDirectoryRead = (path: string, signal?: AbortSignal, subjectType?: string) => Promise<unknown>;

export interface DataScopeSubjectName {
  id: string;
  name: string;
  parentId?: string | null;
  path?: { type: string; id: string; name: string }[] | null;
}

interface DirectoryPage {
  items: DataScopeSubjectName[];
  hasMore: boolean;
  currentValues: DataScopeSubjectName[];
}

export interface DataScopeSubjectSearchResult {
  items: DataScopeSubjectName[];
  options: DataScopeSubjectName[];
  hasMore: boolean;
}

export type DataScopeSubjectNames = ReadonlyMap<string, ReadonlyMap<string, DataScopeSubjectName>>;
const currentValueBatchSize = 10;

// transport 由管理入口提供；目录不持有资格、跨请求缓存或 UI 状态。
async function readDirectory(read: DataScopeSubjectDirectoryRead, type: string, parameters: URLSearchParams, signal?: AbortSignal) {
  signal?.throwIfAborted();
  const result = await read(`/subjects/${encodeURIComponent(type)}?${parameters}`, signal, type) as DirectoryPage;
  signal?.throwIfAborted();
  return result;
}

export async function searchDataScopeSubjects(read: DataScopeSubjectDirectoryRead,
  request: { type: string; query: string; page: number; currentId?: string | null }, signal?: AbortSignal): Promise<DataScopeSubjectSearchResult> {
  const parameters = new URLSearchParams({ query: request.query, page: String(request.page) });
  if (request.currentId) parameters.append('currentIds', request.currentId);
  const result = await readDirectory(read, request.type, parameters, signal);
  return { items: result.items, hasMore: result.hasMore,
    options: [...new Map([...result.items, ...result.currentValues].map(item => [item.id, item])).values()] };
}

export async function resolveDataScopeSubjects(read: DataScopeSubjectDirectoryRead,
  subjects: readonly { type: string; subjectId: string | null }[], signal?: AbortSignal): Promise<DataScopeSubjectNames> {
  signal?.throwIfAborted();
  const groups = new Map<string, Set<string>>();
  for (const subject of subjects) {
    if (!subject.subjectId) continue;
    if (!groups.has(subject.type)) groups.set(subject.type, new Set());
    groups.get(subject.type)!.add(subject.subjectId);
  }
  const names = new Map<string, Map<string, DataScopeSubjectName>>();
  // 回显不限制主体总量；串行小批读取避免长 URL 和无界并发，失败不发布部分名称。
  for (const [type, group] of groups) {
    const ids = [...group];
    const values = new Map<string, DataScopeSubjectName>();
    for (let offset = 0; offset < ids.length; offset += currentValueBatchSize) {
      const parameters = new URLSearchParams({ page: '1' });
      for (const id of ids.slice(offset, offset + currentValueBatchSize)) parameters.append('currentIds', id);
      const result = await readDirectory(read, type, parameters, signal);
      for (const value of result.currentValues) values.set(value.id, value);
    }
    names.set(type, values);
  }
  return names;
}
