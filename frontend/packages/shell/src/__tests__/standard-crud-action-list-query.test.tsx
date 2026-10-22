// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type StandardCrudActionContext, type StandardCrudListQueryRequest } from '@icp-platform/sdk';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import { platformTestCatalogs } from './localizationTestCatalogs';

let root: Root | undefined;
let container: HTMLDivElement | undefined;
afterEach(async () => {
  await act(async () => root?.unmount());
  container?.remove();
  vi.restoreAllMocks();
});

type Row = { id: string; ENAME: string };
const entry = 'ListQuery/navigation:records';
const outputPosition = `${entry}/surface:list.pageActions/action:outputSelected`;
// 此处只验证公共宿主的动作上下文；实际履历 authoring 与安全输出由所属模块的后续消费者验证。
const view = { type: 'view', view: { key: 'list-query.records', resourceKey: 'list-query.record',
  dicKey: 'ListQuery', titleKey: 'Title', mode: 'list', model: 'ListQueryRecord',
  query: { fields: [{ key: 'keyword', labelKey: 'Keyword', source: 'preset', preset: 'keyword' }],
    defaultSort: { field: 'ENAME', direction: 'asc' }, defaultPageSize: 20 },
  columns: [{ key: 'name', labelKey: 'Name', source: 'model', field: 'ENAME', sortable: true }],
  rowActions: [], pageActions: [{ key: 'outputSelected', labelKey: 'OutputSelected', handlerKey: 'outputSelected', requiresSelection: true }] } };
const model = { type: 'model', ListQueryRecord: { fields: [
  { name: 'ENAME', storage: { type: 'string' }, uiType: 'text' },
] } };

async function mount(review = false, preview = false) {
  const activeView = { ...view, view: { ...view.view, pageActions: view.view.pageActions.map(action => ({ ...action, ...(preview ? { standardAction: 'view', handlerKey: 'outputSelected', inputKey: 'preview', requiresSelection: false } : review ? { inputKey: 'outputNote' } : {}) })) } };
  const list = vi.fn(async (query?: StandardCrudListQueryRequest) => ({
    items: [{ id: query?.pageIndex === 2 ? 'event-2' : 'event-1', ENAME: '操作记录' }], total: 40,
  }));
  const output = vi.fn(async (_context: StandardCrudActionContext<unknown, Row>) => undefined);
  vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([{
    metadata: { moduleId: 'ListQuery', displayName: 'List query', version: '1.0.0' },
    standardCrudResources: [{ resourceKey: 'list-query.record', listViewKey: view.view.key,
      dataAdapter: { list, get: vi.fn(), getRowId: (row: Row) => row.id }, actionHandlers: { outputSelected: output },
      actionInputRenderers: { preview: Object.assign(context => <output aria-label="输出范围">{JSON.stringify({ query: context.listQuery, ids: context.selectedRows?.map(row => (row as Row).id) })}</output>, { readOnly: true }), outputNote: ({ value, onChange }) => <input aria-label="输出备注" value={String(value ?? '')}
        onChange={event => onChange(event.target.value)} /> } }],
  }]);
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" catalogs={{ zhs: {
    ...platformTestCatalogs.zhs, 'ListQuery.Title': '操作履历', 'ListQuery.Keyword': '描述',
    'ListQuery.Name': '记录', 'ListQuery.OutputSelected': '导出选中',
  } }} supportedLanguages={[{ code: 'zhs', displayName: '中文' }]}>
    <AuthorizationProvider permissions={[]} featurePositions={[entry, outputPosition]}><PlatformQueryProvider scopeKey="actor-1">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'ListQuery', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
        features: [{ key: 'outputSelected', kind: 'action', position: outputPosition, parentPosition: entry, actionScope: 'list.pageActions' }] }] },
        navDocuments: [], viewDocuments: [activeView], modelDocuments: [model] }}>
        <FeaturePlacementContext.Provider value={{ entryPosition: entry }}><StandardCrudListPageHost viewKey={view.view.key} /></FeaturePlacementContext.Provider>
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider>
  </LocalizationProvider></MemoryRouter>));
  await vi.waitFor(() => expect(container!.querySelector('tr[data-row-key="event-1"]')).not.toBeNull());
  return { list, output };
}

async function clickButton(label: string) {
  const button = [...container!.querySelectorAll('button')].find(element => element.textContent?.replace(/\s/g, '') === label);
  expect(button).toBeDefined();
  await act(async () => button!.click());
}
async function typeFilter(value: string) {
  const input = container!.querySelector<HTMLInputElement>('.np-crud-search-fields input')!;
  expect(input).not.toBeNull();
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}
async function selectRow(id: string) {
  const checkbox = container!.querySelector<HTMLInputElement>(`tr[data-row-key="${id}"] input[type="checkbox"]`)!;
  expect(checkbox).not.toBeNull();
  await act(async () => checkbox.click());
}

it('outputs selected records with the applied query rather than an unsubmitted filter draft', async () => {
  const { list, output } = await mount();
  await typeFilter('已应用描述');
  await clickButton('搜索');
  await vi.waitFor(() => expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ keyword: '已应用描述', pageIndex: 1 }), expect.anything()));
  await vi.waitFor(() => expect(container!.querySelector<HTMLInputElement>('tr[data-row-key="event-1"] input')?.disabled).toBe(false));
  await typeFilter('尚未搜索的草稿');
  await selectRow('event-1');
  await clickButton('导出选中');
  await vi.waitFor(() => expect(output).toHaveBeenCalledOnce());
  const submitted = output.mock.calls[0][0];
  expect(submitted).toMatchObject({
    featurePosition: outputPosition, selectedRows: [{ id: 'event-1' }],
    listQuery: { keyword: '已应用描述', pageIndex: 1, pageSize: 20, sortBy: 'ENAME', sortDirection: 'asc' },
  });
  await typeFilter('后续草稿');
  await clickButton('搜索');
  await vi.waitFor(() => expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ keyword: '后续草稿' }), expect.anything()));
  expect(submitted).toMatchObject({ listQuery: { keyword: '已应用描述', pageIndex: 1 } });
});

it('clears the previous selection on paging and outputs the newly selected page with its own query', async () => {
  const { list, output } = await mount();
  await selectRow('event-1');
  await act(async () => container!.querySelector<HTMLButtonElement>('.ant-pagination-next button')!.click());
  await vi.waitFor(() => expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ pageIndex: 2 }), expect.anything()));
  await vi.waitFor(() => expect(container!.querySelector('tr[data-row-key="event-2"]')).not.toBeNull());
  await clickButton('导出选中');
  expect(output).not.toHaveBeenCalled();
  await selectRow('event-2');
  await clickButton('导出选中');
  await vi.waitFor(() => expect(output).toHaveBeenCalledOnce());
  expect(output.mock.calls[0][0]).toMatchObject({ selectedRows: [{ id: 'event-2' }], listQuery: { pageIndex: 2, pageSize: 20 } });
});

it('carries the same selected query through the standard header action review and confirmation', async () => {
  const { list, output } = await mount(true);
  await typeFilter('复核已应用');
  await clickButton('搜索');
  await vi.waitFor(() => expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ keyword: '复核已应用' }), expect.anything()));
  await vi.waitFor(() => expect(container!.querySelector<HTMLInputElement>('tr[data-row-key="event-1"] input')?.disabled).toBe(false));
  await typeFilter('复核未应用草稿');
  await selectRow('event-1');
  await clickButton('导出选中');
  await vi.waitFor(() => expect(document.querySelector('input[aria-label="输出备注"]')).not.toBeNull());
  expect(output).not.toHaveBeenCalled();
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-footer .ant-btn-primary')!.click());
  await vi.waitFor(() => expect(output).toHaveBeenCalledOnce());
  expect(output.mock.calls[0][0]).toMatchObject({ featurePosition: outputPosition, selectedRows: [{ id: 'event-1' }],
    listQuery: { keyword: '复核已应用', pageIndex: 1, pageSize: 20 } });
  await vi.waitFor(() => expect(document.querySelector('input[aria-label="输出备注"]')).toBeNull());
});

it('opens a readonly preview without selection and supplies only the captured applied query and selected rows', async () => {
  const { list, output } = await mount(false, true);
  await typeFilter('预览已应用');
  await clickButton('搜索');
  await vi.waitFor(() => expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ keyword: '预览已应用' }), expect.anything()));
  await typeFilter('预览草稿');
  await clickButton('导出选中');
  await vi.waitFor(() => expect(document.querySelector('output[aria-label="输出范围"]')).not.toBeNull());
  expect(JSON.parse(document.querySelector('output[aria-label="输出范围"]')!.textContent!)).toMatchObject({ query: { keyword: '预览已应用', pageIndex: 1, pageSize: 20 }, ids: [] });
  expect(output).not.toHaveBeenCalled();
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-footer button')!.click());
  await vi.waitFor(() => expect(document.querySelector('output[aria-label="输出范围"]')).toBeNull());
  await selectRow('event-1');
  await clickButton('导出选中');
  await vi.waitFor(() => expect(document.querySelector('output[aria-label="输出范围"]')).not.toBeNull());
  expect(JSON.parse(document.querySelector('output[aria-label="输出范围"]')!.textContent!)).toMatchObject({ query: { keyword: '预览已应用' }, ids: ['event-1'] });
  expect(output).not.toHaveBeenCalled();
});
