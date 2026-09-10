import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { changesRenderer, createBusinessChangeResource, referenceRenderer } from '../businessChangeResource';

describe('业务变更记录只读资源', () => {
  it('保留原始标识与服务端展示伴随值，并转发组合筛选', async () => {
    const fetcher = vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'record-1', TargetId: 'person-1', TargetId_DisplayValue: '当前姓名' }], total: 1,
    })));
    const resource = createBusinessChangeResource(fetcher);
    const page = await resource.dataAdapter.list({ TargetId: 'person-1', ActorId: 'actor-1', EventType: 'person.created', From: '2026-09-10T00:00:00Z' });
    expect(page.items[0]).toMatchObject({ id: 'record-1', TargetId: 'person-1', TargetId_DisplayValue: '当前姓名' });
    const url = new URL(String(fetcher.mock.calls[0][0]), 'http://localhost');
    expect(url.searchParams.get('TargetId')).toBe('person-1');
    expect(url.searchParams.get('ActorId')).toBe('actor-1');
    expect(url.searchParams.get('EventType')).toBe('person.created');
    expect(url.searchParams.get('From')).toBe('2026-09-10T00:00:00Z');
  });

  it('服务错误与无效响应不会投影为空列表', async () => {
    for (const response of [new Response('unavailable', { status: 500 }), new Response(JSON.stringify({ items: [{}], total: 1 }))]) {
      const resource = createBusinessChangeResource(vi.fn<Parameters<typeof fetch>, ReturnType<typeof fetch>>().mockResolvedValue(response));
      await expect(resource.dataAdapter.list()).rejects.toThrow();
    }
  });

  it('当前名称与稳定标识并列，目录缺失时只保留标识', () => {
    expect(referenceRenderer({ fieldKey: 'ActorId', value: 'actor-1', detailFields: { ActorId_DisplayValue: '当前用户' } })).toBe('当前用户（actor-1）');
    expect(referenceRenderer({ fieldKey: 'ActorId', value: 'actor-1', detailFields: { ActorId_DisplayValue: null } })).toBe('actor-1');
  });

  it('明细保留空值、原始日历日期、UTC时间及引用标识', () => {
    const html = renderToStaticMarkup(changesRenderer({ blockKey: 'changes', title: '变化明细', block: { status: 'ready', data: [
      { field: 'PositionId', before: null, after: { kind: 'position', value: null } },
      { field: 'BirthDate', before: null, after: { kind: 'calendarDate', value: '2000-02-29' }, afterDisplay: '2000/2/29' },
      { field: 'EmploymentStartedAt', before: null, after: { kind: 'instant', value: '2026-09-10T00:00:00Z' }, afterDisplay: '2026/9/10 08:00' },
      { field: 'CompanyId', before: null, after: { kind: 'company', value: 'company-1' }, afterDisplay: '当前公司' },
    ] } }));
    for (const text of ['空值', '2000-02-29', '2000/2/29', '2026-09-10T00:00:00Z', '2026/9/10 08:00', '当前公司（company-1）']) expect(html).toContain(text);
  });
});
