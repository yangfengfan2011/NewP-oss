import { describe, expect, it, vi } from 'vitest';
import exampleBindings from '../index';
import { createExampleRecordStandardCrudResource } from '../standard-crud/exampleRecordStandardCrudResource';

describe('Example persisted record Standard CRUD resource', () => {
  it('publishes list and detail capabilities backed by the canonical REST adapter', async () => {
    expect(exampleBindings.standardCrudCapabilities?.map(({ kind, targetKey }) => ({ kind, targetKey }))).toEqual([
      { kind: 'standard-list', targetKey: 'example.runtime-workload.list' },
      { kind: 'standard-list', targetKey: 'example.record.list' },
      { kind: 'standard-crud-detail', targetKey: 'example.record.detail' },
    ]);

    const loadedList = await exampleBindings.standardCrudCapabilities?.[1]?.load();
    const loadedDetail = await exampleBindings.standardCrudCapabilities?.[2]?.load();
    expect(loadedList?.default).toMatchObject({
      resourceKey: 'example.record',
      listViewKey: 'example.record.list',
      detailViewKey: 'example.record.detail',
    });
    expect(loadedDetail?.default).toBe(loadedList?.default);

    const fetcher = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{
          EID: 'ExampleRecord_1',
          ECODE: 'EX-001',
          ENAME: '示例记录',
          ReviewerUserEID: 'User_1',
          ReviewerUserEID_DisplayValue: 'Alice',
          RecordSource: 'Legacy',
        }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: {
          EID: 'ExampleRecord_1',
          ECODE: 'EX-001',
          ENAME: '示例记录',
          ReviewerUserEID: 'User_1',
          ReviewerUserEID_DisplayValue: 'Alice',
          RecordSource: 'Legacy',
        },
        summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const resource = createExampleRecordStandardCrudResource(fetcher as unknown as typeof fetch);

    const list = await resource.dataAdapter.list({ keyword: '示例', pageIndex: 1, pageSize: 20 });
    const detail = await resource.dataAdapter.get('ExampleRecord_1');

    expect(fetcher.mock.calls[0]?.[0]).toBe('/api/example/records?keyword=%E7%A4%BA%E4%BE%8B&pageIndex=1&pageSize=20');
    expect(fetcher.mock.calls[1]?.[0]).toBe('/api/example/records/ExampleRecord_1');
    expect(list.items).toEqual([{
      id: 'ExampleRecord_1',
      EID: 'ExampleRecord_1',
      ECODE: 'EX-001',
      ENAME: '示例记录',
      ReviewerUserEID: 'User_1',
      ReviewerUserEID_DisplayValue: 'Alice',
      RecordSource: 'Legacy',
    }]);
    expect(detail.normalized.fields).toMatchObject({
      ECODE: 'EX-001',
      ENAME: '示例记录',
      ReviewerUserEID: 'User_1',
      ReviewerUserEID_DisplayValue: 'Alice',
      RecordSource: 'Legacy',
    });
  });
});
