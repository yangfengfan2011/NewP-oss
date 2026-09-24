export interface DataScopeViewLabels {
  resourceKey: string; dicKey: string; titleKey: string; mode: string;
  fields: Record<string, string>;
}
interface ViewDocument {
  view: { model?: string; resourceKey?: string; dicKey?: string; titleKey?: string; mode?: string;
    columns?: { field?: string; labelKey?: string }[];
    sections?: { fields?: (string | { field: string; labelKey?: string })[] }[] };
}
interface MetadataManifest { modules: { moduleId: string; metadataBasePath: string; viewFiles: string[] }[] }
type Translate = (key: string, fallback?: string) => string;

/** 从公开 runtime 视图声明定位词典，不依赖 Shell 私有上下文，也不推测模型与词典的命名关系。 */
export async function loadDataScopeViewLabels(fetcher: typeof fetch, resources: { resourceKey: string; modelKey: string }[], signal: AbortSignal): Promise<DataScopeViewLabels[]> {
  const read = async <T,>(url: string): Promise<T> => {
    const response = await fetcher(url, { credentials: 'same-origin', signal });
    if (!response.ok) throw new Error('资源与属性名称读取失败，请刷新重试。');
    return response.json() as Promise<T>;
  };
  const manifest = await read<MetadataManifest>('/plugins/metadata-runtime-manifest.json');
  const owners = new Set(resources.map(resource => resource.modelKey.split('/')[0]));
  const labels: DataScopeViewLabels[] = [];
  // 按涉及的模块串行读取视图；已取消的页面不再发起下一次读取。
  for (const module of manifest.modules.filter(module => owners.has(module.moduleId))) {
    for (const file of module.viewFiles) {
      signal.throwIfAborted();
      const { view } = await read<ViewDocument>(`${module.metadataBasePath}/${file}`);
      if (!view.dicKey || !['list', 'detail'].includes(view.mode ?? '')) continue;
      const modelKey = view.model?.includes('/') ? view.model : `${module.moduleId}/${view.model}`;
      const matching = resources.filter(resource => resource.modelKey === modelKey);
      const resourceKey = view.resourceKey ?? (matching.length === 1 ? matching[0].resourceKey : undefined);
      if (!resourceKey) continue;
      const fields: Record<string, string> = {};
      for (const column of view.columns ?? []) if (column.field && column.labelKey) fields[column.field] = column.labelKey;
      for (const section of view.sections ?? []) for (const field of section.fields ?? []) {
        if (typeof field === 'object' && field.labelKey) fields[field.field] = field.labelKey;
      }
      labels.push({ resourceKey, dicKey: view.dicKey, titleKey: view.titleKey ?? 'Title', mode: view.mode!, fields });
    }
  }
  return labels.sort((left, right) => Number(left.mode !== 'list') - Number(right.mode !== 'list'));
}

/** 当前语言和默认语言的回退由 LocalizationProvider 统一处理；传输标识始终不变。 */
export function dataScopeLabels(views: DataScopeViewLabels[], resourceKey: string, t: Translate) {
  const matching = views.filter(view => view.resourceKey === resourceKey);
  const firstTranslation = (keys: string[], fallback: string) => {
    for (const key of keys) { const value = t(key, ''); if (value) return value; }
    return fallback;
  };
  return {
    resource: (fallback: string) => firstTranslation(matching.map(view => `${view.dicKey}.${view.titleKey}`), fallback),
    field: (name: string) => firstTranslation(matching.flatMap(view => [
      `${view.dicKey}.Field_${name}`, ...(view.fields[name] ? [`${view.dicKey}.${view.fields[name]}`] : []),
    ]), name),
  };
}
