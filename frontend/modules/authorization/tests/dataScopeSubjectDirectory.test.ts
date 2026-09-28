import { expect, it, vi } from 'vitest';
import { resolveDataScopeSubjects, searchDataScopeSubjects } from '../../../packages/ui/src/dataScopeSubjectDirectory';

it('encodes a search and keeps page candidates distinct from merged current options', async () => {
  const controller = new AbortController();
  const current = { id: 'saved', name: '当前名称', path: [{ type: 'company', id: 'c', name: '公司' }] };
  const items = [{ id: 'saved', name: '候选名称' }, { id: 'other', name: '同名部门' }];
  const read = vi.fn(async () => ({ items, hasMore: true, currentValues: [current, { id: 'off-page', name: '页外主体' }] }));
  const result = await searchDataScopeSubjects(read, { type: 'department/custom', query: '甲 & 乙', page: 2, currentId: 'saved & id' }, controller.signal);
  expect(read).toHaveBeenCalledWith('/subjects/department%2Fcustom?query=%E7%94%B2+%26+%E4%B9%99&page=2&currentIds=saved+%26+id', controller.signal);
  expect(result.items).toBe(items);
  expect(result.options).toEqual([current, items[1], { id: 'off-page', name: '页外主体' }]);
  expect(result.hasMore).toBe(true);
});

it('groups and deduplicates identities without merging the same id across subject types', async () => {
  const subjects = Array.from({ length: 21 }, (_, index) => ({ type: 'department', subjectId: `d-${index}` }));
  let active = 0;
  let maximumActive = 0;
  const requests: { type: string; ids: string[] }[] = [];
  const read = vi.fn(async (path: string) => {
    active++;
    maximumActive = Math.max(maximumActive, active);
    await Promise.resolve();
    const url = new URL(path, 'https://directory.test');
    const ids = url.searchParams.getAll('currentIds');
    const type = url.pathname.split('/').pop()!;
    requests.push({ type, ids });
    active--;
    return { items: [{ id: 'candidate', name: '不在回显内' }], hasMore: false,
      currentValues: ids.filter(id => id !== 'missing').map(id => ({ id, name: `${type}/${id}` })) };
  });
  const result = await resolveDataScopeSubjects(read, [...subjects, subjects[0], { type: 'all', subjectId: null },
    { type: 'user', subjectId: 'd-0' }, { type: 'user', subjectId: 'missing' }]);
  expect(requests).toEqual([
    { type: 'department', ids: subjects.slice(0, 10).map(subject => subject.subjectId) },
    { type: 'department', ids: subjects.slice(10, 20).map(subject => subject.subjectId) },
    { type: 'department', ids: ['d-20'] }, { type: 'user', ids: ['d-0', 'missing'] },
  ]);
  expect(maximumActive).toBe(1);
  expect(result.get('department')?.size).toBe(21);
  expect(result.get('department')?.get('d-0')?.name).toBe('department/d-0');
  expect(result.get('user')?.get('d-0')?.name).toBe('user/d-0');
  expect(result.get('user')?.has('missing')).toBe(false);
  expect(result.get('department')?.has('candidate')).toBe(false);
});

it('does not read for subjects without identities and rejects an already cancelled request', async () => {
  const read = vi.fn();
  expect(await resolveDataScopeSubjects(read, [{ type: 'all', subjectId: null }])).toEqual(new Map());
  const controller = new AbortController();
  controller.abort();
  await expect(resolveDataScopeSubjects(read, [{ type: 'user', subjectId: 'u' }], controller.signal)).rejects.toMatchObject({ name: 'AbortError' });
  await expect(searchDataScopeSubjects(read, { type: 'user', query: '', page: 1 }, controller.signal)).rejects.toMatchObject({ name: 'AbortError' });
  expect(read).not.toHaveBeenCalled();
});

it('cancels between batches even when an adapter finishes after abort', async () => {
  let finish!: (value: unknown) => void;
  const read = vi.fn((_path: string, _signal?: AbortSignal) => new Promise(resolve => { finish = resolve; }));
  const controller = new AbortController();
  const pending = resolveDataScopeSubjects(read, Array.from({ length: 11 }, (_, index) => ({ type: 'user', subjectId: String(index) })), controller.signal);
  const rejected = expect(pending).rejects.toMatchObject({ name: 'AbortError' });
  controller.abort();
  finish({ currentValues: [{ id: '0', name: '过期用户' }] });
  await rejected;
  expect(read).toHaveBeenCalledTimes(1);
  expect(read.mock.calls[0][1]).toBe(controller.signal);
});

it('does not return a stale search when the transport ignores cancellation', async () => {
  let finish!: (value: unknown) => void;
  const controller = new AbortController();
  const pending = searchDataScopeSubjects(() => new Promise(resolve => { finish = resolve; }), { type: 'user', query: '', page: 1 }, controller.signal);
  const rejected = expect(pending).rejects.toMatchObject({ name: 'AbortError' });
  controller.abort();
  finish({ items: [], currentValues: [], hasMore: false });
  await rejected;
});

it('propagates transport errors without partial results, retry or persistent names', async () => {
  const failure = new Error('当前没有对象授权资格。');
  const read = vi.fn().mockResolvedValueOnce({ currentValues: [{ id: '0', name: '旧用户' }] }).mockRejectedValueOnce(failure);
  const subjects = Array.from({ length: 11 }, (_, index) => ({ type: 'user', subjectId: String(index) }));
  await expect(resolveDataScopeSubjects(read, subjects)).rejects.toBe(failure);
  expect(read).toHaveBeenCalledTimes(2);
  read.mockResolvedValue({ currentValues: [] });
  expect((await resolveDataScopeSubjects(read, [subjects[0]])).get('user')?.size).toBe(0);
  await expect(searchDataScopeSubjects(async () => { throw failure; }, { type: 'user', query: '', page: 1 })).rejects.toBe(failure);
});
