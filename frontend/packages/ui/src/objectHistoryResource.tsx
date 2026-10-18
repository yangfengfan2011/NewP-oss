import { Alert } from 'antd';
import { formatRequestError, platformFetch, useLocalization } from '@icp-platform/core';
import { isStandardCrudRecord, StandardCrudRequestError, type StandardCrudActionInputRenderContext, type StandardCrudActionInputRenderer,
  type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import ObjectHistoryOutput from './ObjectHistoryOutput';
import ObjectHistoryDetails, { ObjectHistoryValue, type ObjectHistoryDisplayField } from './ObjectHistoryDetails';
import { objectHistoryFilter, objectHistoryOutputQuery, readObjectHistoryDetail, readObjectHistoryItem, readObjectHistoryPage,
  type ObjectHistoryTarget } from './objectHistoryRequest';

export interface ObjectHistoryResourceOptions {
  precheck?: import('@icp-platform/sdk').StandardCrudActionPrecheck;
  resourceKey: string;
  listViewKey: string;
  resolveTarget: (queryScope: Record<string, unknown> | undefined) => ObjectHistoryTarget;
  fetcher?: typeof fetch;
}
const sourceKey = (target: ObjectHistoryTarget, featurePosition?: string) => JSON.stringify([target.resourceKey, target.id, target.basePath, featurePosition]);
const fields: ObjectHistoryDisplayField[] = ['eventType', 'occurredAt', 'objectType', 'actor', 'target', 'description', 'ipAddress'];

function HistoryDetailInput({ options, context }: { options: ObjectHistoryResourceOptions; context: StandardCrudActionInputRenderContext }) {
  const { t } = useLocalization();
  try {
    const target = options.resolveTarget(context.queryScope);
    const row = context.row;
    if (!isStandardCrudRecord(row) || row.historySource !== sourceKey(target, context.featurePosition) || !isStandardCrudRecord(row.historyFilter))
      throw new StandardCrudRequestError('操作履历来源已变化。', 409, {}, { reasonKey: 'ObjectHistory.SourceChanged' });
    const item = readObjectHistoryItem(row.historyItem);
    return <ObjectHistoryDetails target={target} recordId={item.id} filter={objectHistoryFilter(row.historyFilter)}
      featurePosition={context.featurePosition} dataAccess={context.action.dataAccess} fetcher={options.fetcher} />;
  } catch (error) { return <Alert type="error" message={formatRequestError(error, t)} />; }
}

function HistoryOutputInput({ options, context }: { options: ObjectHistoryResourceOptions; context: StandardCrudActionInputRenderContext }) {
  const { t } = useLocalization();
  try {
    const target = options.resolveTarget(context.queryScope);
    const ids = (context.selectedRows ?? []).map(row => {
      if (!isStandardCrudRecord(row) || row.historySource !== sourceKey(target, context.featurePosition))
        throw new StandardCrudRequestError('操作履历来源已变化。', 409, {}, { reasonKey: 'ObjectHistory.SourceChanged' });
      return readObjectHistoryItem(row.historyItem).id;
    });
    return <ObjectHistoryOutput target={target} featurePosition={context.featurePosition} filter={objectHistoryOutputQuery(context.listQuery)}
      selectedIds={ids} dataAccessDeclarations={context.dataAccessDeclarations} fetcher={options.fetcher} />;
  } catch (error) { return <Alert type="error" message={formatRequestError(error, t)} />; }
}

/** model-less 标准列表只接收安全投影；详情沿现只读动作输入，不引入管理员 schema 或新页面宿主。 */
export function createObjectHistoryResource(options: ObjectHistoryResourceOptions): StandardCrudResourceRegistration {
  const fetcher = options.fetcher ?? platformFetch as typeof fetch;
  const detailInput: StandardCrudActionInputRenderer = Object.assign((context: StandardCrudActionInputRenderContext) =>
    <HistoryDetailInput options={options} context={context} />, { readOnly: true, presentation: 'drawer' as const });
  return { resourceKey: options.resourceKey, listViewKey: options.listViewKey,
    ...(options.precheck ? { actionPrechecks: { view: options.precheck, output: options.precheck } } : {}),
    dataAdapter: {
      list: async (query = {}, context) => {
        const target = options.resolveTarget(context?.queryScope);
        const page = await readObjectHistoryPage(target, query, context ?? {}, fetcher);
        return { total: page.total, page: page.pageIndex, pageSize: page.pageSize, items: page.items.map(item => ({ id: item.id,
          ...Object.fromEntries(fields.map(field => [field, item[field]])), historyItem: item,
          historyFilter: objectHistoryFilter(query), historySource: sourceKey(target, context?.featurePosition) })) };
      },
      get: async (id, context) => {
        const target = options.resolveTarget(context?.queryScope);
        const item = await readObjectHistoryDetail(target, id, objectHistoryFilter(context?.queryScope), context ?? {}, fetcher);
        const normalized = { fields: { ...item }, summaries: {} };
        return { normalized, value: normalized };
      },
      getRowId: row => {
        if (typeof row.id !== 'string') throw new StandardCrudRequestError('操作履历记录标识无效。', 502);
        return row.id;
      },
    },
    actionHandlers: { view: () => undefined },
    actionInputRenderers: { objectHistoryDetails: detailInput, objectHistoryOutput: Object.assign((context: StandardCrudActionInputRenderContext) =>
      <HistoryOutputInput options={options} context={context} />, { readOnly: true }) },
    listFieldRenderers: Object.fromEntries(fields.map(field => [field, ({ row }: { row: Record<string, unknown> }) => <ObjectHistoryValue item={readObjectHistoryItem(row.historyItem)} field={field} />])),
  };
}
