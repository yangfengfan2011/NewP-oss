import { requestAuthorization } from './authorizationRequest';

export interface DataScopeViewLabels {
  resourceKey: string; dicKey: string; titleKey: string; mode: string;
  fields: Record<string, string>;
  enumerations?: Record<string, string>;
}
interface ViewDocument {
  view: { model?: string; resourceKey?: string; dicKey?: string; titleKey?: string; mode?: string;
    columns?: { field?: string; labelKey?: string }[];
    sections?: { fields?: (string | { field: string; labelKey?: string })[] }[] };
}
interface MetadataManifest { modules: { moduleId: string; metadataBasePath: string; viewFiles: string[]; modelFiles?: string[] }[] }
type Translate = (key: string, fallback?: string) => string;

/** 从公开 runtime 视图声明定位词典，不依赖 Shell 私有上下文，也不推测模型与词典的命名关系。 */
export async function loadDataScopeViewLabels(fetcher: typeof fetch, resources: { resourceKey: string; modelKey: string }[], signal: AbortSignal): Promise<DataScopeViewLabels[]> {
  const read = async <T,>(url: string): Promise<T> => {
    return requestAuthorization(fetcher, url, { credentials: 'same-origin', signal }) as Promise<T>;
  };
  const manifest = await read<MetadataManifest>('/plugins/metadata-runtime-manifest.json');
  const owners = new Set(resources.map(resource => resource.modelKey.split('/')[0]));
  const labels: DataScopeViewLabels[] = [];
  // 按涉及的模块串行读取视图；已取消的页面不再发起下一次读取。
  for (const module of manifest.modules.filter(module => owners.has(module.moduleId))) {
    // 与 View 共用本轮 manifest；只取显式 dataSource 身份，合法值仍由数据范围目录决定。
    for (const file of module.modelFiles ?? []) {
      signal.throwIfAborted();
      const document = await read<Record<string, unknown>>(`${module.metadataBasePath}/${file}`);
      for (const resource of resources.filter(resource => resource.modelKey.startsWith(`${module.moduleId}/`))) {
        const model = document[resource.modelKey.slice(module.moduleId.length + 1)] as {
          fields?: { name: string; dataSource?: { type: string; key?: string } }[];
        } | undefined;
        if (!model?.fields) continue;
        const enumerations = Object.fromEntries(model.fields.filter(field => field.dataSource?.type === 'enum' && field.dataSource.key)
          .map(field => [field.name, field.dataSource!.key!]));
        labels.push({ resourceKey: resource.resourceKey, dicKey: '', titleKey: '', mode: 'model', fields: {}, enumerations });
      }
    }
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
    resource: (fallback: string) => firstTranslation(matching.filter(view => view.dicKey).map(view => `${view.dicKey}.${view.titleKey}`), fallback),
    field: (name: string) => firstTranslation(matching.filter(view => view.dicKey).flatMap(view => [
      `${view.dicKey}.Field_${name}`, ...(view.fields[name] ? [`${view.dicKey}.${view.fields[name]}`] : []),
    ]), name),
    enumeration: (name: string) => matching.find(view => view.enumerations?.[name])?.enumerations?.[name],
  };
}
