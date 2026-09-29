// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useStandardCrudEditSession, type StandardCrudEditSessionOptions } from '../standard-crud/useStandardCrudEditSession';
import type { StandardCrudDetailReloadResult } from '../standard-crud/standardCrudDetailQuery';
import { buildStandardCrudDetailFormValues } from '../standard-crud/standardCrudDetailFormValues';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('Standard CRUD edit session', () => {
  let root: Root;
  let container: HTMLDivElement;
  let session: ReturnType<typeof useStandardCrudEditSession>;
  let options: StandardCrudEditSessionOptions;
  const original = { EID: '1', Name: 'Original', Revision: 7 };
  const latest = { EID: '1', Name: 'Latest', Revision: 8 };
  const result = (fields: Record<string, unknown>): StandardCrudDetailReloadResult => ({
    status: 'success', detail: { normalized: { fields, summaries: {} }, value: fields },
  });

  function Harness() { session = useStandardCrudEditSession(options); return null; }
  async function render(overrides: Partial<StandardCrudEditSessionOptions> = {}) {
    if (!container) {
      container = document.createElement('div');
      document.body.appendChild(container);
      root = createRoot(container);
      options = {
        identity: 'record:1', snapshot: original,
        fields: [{ key: 'name', modelField: 'Name', labelKey: 'Name', kind: 'text' }],
        buildValues: buildStandardCrudDetailFormValues,
        reload: vi.fn(async () => result(latest)),
      };
    }
    options = { ...options, ...overrides };
    await act(async () => root.render(<Harness />));
  }
  afterEach(async () => {
    if (root) await act(async () => root.unmount());
    container?.remove();
    container = undefined!;
  });

  it('keeps values, dirty state and the original write basis through presentation rereads', async () => {
    await render();
    expect(session.dirty).toBe(false);
    await act(async () => session.change('name', 'Draft'));
    await render({ snapshot: undefined });
    await render({ snapshot: latest, fields: [{ ...options.fields[0], labelKey: '姓名' }] });
    expect(session.draft).toEqual({ values: { name: 'Draft' }, detailFields: original });
    expect(session.dirty).toBe(true);
    expect(options.reload).not.toHaveBeenCalled();
  });

  it('adopts an explicit successful reload even when the snapshot object is unchanged', async () => {
    const reload = vi.fn(async () => result(original));
    await render({ reload });
    await act(async () => session.change('name', 'Draft'));
    await act(async () => expect(await session.reload()).toBe(true));
    expect(session.draft).toEqual({ values: { name: 'Original' }, detailFields: original });
    expect(session.dirty).toBe(false);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it.each(['failed', 'cancelled'] as const)('consumes a %s reload without letting a later ordinary read replace the draft', async status => {
    await render({ reload: vi.fn(async () => ({ status })) });
    await act(async () => session.change('name', 'Draft'));
    await act(async () => expect(await session.reload()).toBe(false));
    await render({ snapshot: latest });
    expect(session.draft).toEqual({ values: { name: 'Draft' }, detailFields: original });
    expect(session.dirty).toBe(true);
    expect(session.reloading).toBe(false);
  });

  it('does not start a queued reload after the host has ended the edit session', async () => {
    await render();
    let pending!: Promise<boolean>;
    await act(async () => {
      pending = session.reload();
      options = { ...options, identity: null };
      flushSync(() => root.render(<Harness />));
    });
    expect(await pending).toBe(false);
    expect(options.reload).not.toHaveBeenCalled();
  });

  it.each(['reopen', 'switch'] as const)('ignores a late reload after %s and coalesces repeated reload intent', async action => {
    let complete!: (value: StandardCrudDetailReloadResult) => void;
    const reload = vi.fn(() => new Promise<StandardCrudDetailReloadResult>(resolve => { complete = resolve; }));
    await render({ reload });
    await act(async () => session.change('name', 'Old draft'));
    let pending!: Promise<boolean>;
    await act(async () => { pending = session.reload(); expect(session.reload()).toBe(pending); });
    expect(session.reloading).toBe(true);
    if (action === 'reopen') {
      await render({ identity: null });
      expect(session.draft).toBeNull();
    }
    const next = { EID: action === 'switch' ? '2' : '1', Name: 'Reopened', Revision: 10 };
    await render({ identity: `record:${next.EID}`, snapshot: next });
    await act(async () => session.change('name', 'New draft'));
    await act(async () => complete(result(latest)));
    expect(await pending).toBe(false);
    expect(session.draft).toEqual({ values: { name: 'New draft' }, detailFields: next });
    expect(session.reloading).toBe(false);
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
