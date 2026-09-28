// @vitest-environment jsdom

import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import platformDictionary from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';
import {
  StandardCrudRequestError,
  type StandardCrudOperationResult,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import {
  useStandardCrudMutationExecution,
  type StandardCrudMutationExecution,
} from '../standard-crud/useStandardCrudMutationExecution';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('standard CRUD mutation execution', () => {
  let container: HTMLDivElement;
  let root: Root | null = null;
  const mutationFields = [{ key: 'name', labelKey: 'Name', modelField: 'Name', kind: 'text', trim: true }] as const;

  afterEach(async () => {
    if (root) {
      await act(async () => root?.unmount());
      root = null;
    }
    container?.remove();
  });

  async function renderExecution(
    registration: StandardCrudResourceRegistration,
    refresh: () => void,
    onResult: (result: StandardCrudOperationResult) => void,
    options: { fixedCreateValues?: Record<string, unknown>; onCommitted?: (change: import("@icp-platform/sdk").ResourceChange) => Promise<void> } = {},
  ): Promise<() => StandardCrudMutationExecution> {
    let currentExecution: StandardCrudMutationExecution | undefined;

    function Harness() {
      const execution = useStandardCrudMutationExecution({ registration, refresh, onResult, ...options });
      useEffect(() => {
        currentExecution = execution;
      }, [execution]);
      return null;
    }

    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      const catalogs = { zhs: Object.fromEntries(Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs])) };
      root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]} catalogs={catalogs}><Harness /></LocalizationProvider>);
    });

    return () => {
      if (!currentExecution) throw new Error('mutation execution is not ready');
      return currentExecution;
    };
  }

  it('retains fixed creation context and publishes once even if subsequent reads fail', async () => {
    const create = vi.fn().mockResolvedValue({ operation: 'create', payload: {}, change: { operation: 'create', id: 'd1', after: { CompanyId: 'current', ParentDepartmentId: null } } });
    const committed = vi.fn().mockRejectedValue(new Error('offline'));
    const refresh = vi.fn();
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'organization.department', listViewKey: 'department.list',
      dataAdapter: { list: vi.fn(), get: vi.fn(), getRowId: () => 'd1', create },
      buildMutationValues: values => values,
    };
    const execution = await renderExecution(registration, refresh, vi.fn(), { fixedCreateValues: { CompanyId: 'current', ParentDepartmentId: null }, onCommitted: committed });
    let succeeded = false;
    await act(async () => { succeeded = await execution().execute({ mode: 'create',
      fields: [{ key: 'CompanyId', labelKey: 'Company', modelField: 'CompanyId', kind: 'text', readOnly: true }],
      values: { CompanyId: 'tampered', ParentDepartmentId: 'tampered', Name: 'saved' }, detailFields: {} }); });
    expect(succeeded).toBe(true);
    expect(create).toHaveBeenCalledTimes(1);
    expect(create).toHaveBeenCalledWith({ CompanyId: 'current', ParentDepartmentId: null, Name: 'saved' }, {});
    expect(committed).toHaveBeenCalledTimes(1);
    expect(refresh).not.toHaveBeenCalled();
    expect(execution().error).toBeNull();
    expect(execution().synchronizationError).toContain('已保存，刷新失败');
  });

  it('refreshes once and presents the opaque result after a successful create', async () => {
    const result: StandardCrudOperationResult = {
      operation: 'create',
      payload: { receipt: 'opaque' },
    };
    const create = vi.fn().mockResolvedValue(result);
    const refresh = vi.fn();
    const onResult = vi.fn();
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'sample.record',
      listViewKey: 'sample.record.list',
      dataAdapter: {
        list: vi.fn(),
        get: vi.fn(),
        getRowId: () => 'record-1',
        create,
      },
    };
    const execution = await renderExecution(registration, refresh, onResult);

    let succeeded = false;
    await act(async () => {
      succeeded = await execution().execute({
        mode: 'create',
        fields: [...mutationFields],
        values: { name: ' Alice ' },
        detailFields: {},
      });
    });

    expect(succeeded).toBe(true);
    expect(create).toHaveBeenCalledWith({ Name: 'Alice' }, {});
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(onResult).toHaveBeenCalledWith(result);
    expect(execution().pending).toBe(false);
    expect(execution().error).toBeNull();
  });

  it('removes readonly model fields restored by a mutation builder while preserving extra business values', async () => {
    const create = vi.fn().mockResolvedValue({ operation: 'create' });
    const builtValues = {
      Name: 'Alice',
      CreatedAt: '2026-09-05T01:02:03Z',
      MutationReason: 'manual-entry',
    };
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'sample.record',
      listViewKey: 'sample.record.list',
      buildMutationValues: () => builtValues,
      dataAdapter: {
        list: vi.fn(),
        get: vi.fn(),
        getRowId: () => 'record-1',
        create,
      },
    };
    const execution = await renderExecution(registration, vi.fn(), vi.fn());

    await act(async () => {
      await execution().execute({
        mode: 'create',
        fields: [
          ...mutationFields,
          {
            key: 'createdAt',
            labelKey: 'Created at',
            modelField: 'CreatedAt',
            kind: 'readonly',
            readOnly: true,
          },
        ],
        values: { name: ' Alice ', createdAt: '2026-09-05T01:02:03Z' },
        detailFields: {},
      });
    });

    expect(create).toHaveBeenCalledWith({ Name: 'Alice', MutationReason: 'manual-entry' }, {});
    expect(builtValues).toEqual({
      Name: 'Alice',
      CreatedAt: '2026-09-05T01:02:03Z',
      MutationReason: 'manual-entry',
    });
  });

  it('maps backend field errors to form keys and does not refresh on an ordinary failure', async () => {
    const create = vi.fn().mockRejectedValue(new StandardCrudRequestError('名称不能为空', 400, { Name: '名称不能为空' }));
    const refresh = vi.fn();
    const onResult = vi.fn();
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'sample.record',
      listViewKey: 'sample.record.list',
      dataAdapter: {
        list: vi.fn(),
        get: vi.fn(),
        getRowId: () => 'record-1',
        create,
      },
    };
    const execution = await renderExecution(registration, refresh, onResult);

    let succeeded = false;
    await act(async () => {
      succeeded = await execution().execute({
        mode: 'create',
        fields: [...mutationFields],
        values: { name: ' ' },
        detailFields: {},
      });
    });

    expect(succeeded).toBe(false);
    expect(refresh).not.toHaveBeenCalled();
    expect(onResult).not.toHaveBeenCalled();
    expect(execution().error).toBe('操作失败，请稍后重试。');
    expect(execution().fieldErrors).toEqual({ name: '请检查输入的字段。' });
    expect(execution().conflict).toBe(false);
  });

  it('classifies 409 as a conflict without auto-refreshing the surface', async () => {
    const update = vi.fn().mockRejectedValue(new StandardCrudRequestError('记录已变化', 409, { Name: '请重新确认名称' }));
    const refresh = vi.fn();
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'sample.record',
      listViewKey: 'sample.record.list',
      concurrencyRevisionField: 'Revision',
      dataAdapter: {
        list: vi.fn(),
        get: vi.fn(),
        getRowId: () => 'record-1',
        update,
      },
    };
    const execution = await renderExecution(registration, refresh, vi.fn());

    let succeeded = false;
    await act(async () => {
      succeeded = await execution().execute({
        mode: 'edit',
        id: 'record-1',
        fields: [...mutationFields],
        values: { name: ' Alice ' },
        detailFields: { Revision: 4 },
      });
    });

    expect(succeeded).toBe(false);
    expect(update).toHaveBeenCalledWith('record-1', { Name: 'Alice', Revision: 4 }, { Revision: 4 });
    expect(refresh).not.toHaveBeenCalled();
    expect(execution().conflict).toBe(true);
    expect(execution().fieldErrors).toEqual({ name: '请检查输入的字段。' });
    expect(execution().error).toBe('记录已发生变化，未覆盖服务器上的最新数据。请重新加载后再编辑。');
  });

  it('sends an explicitly cleared optional lookup as null during update', async () => {
    const update = vi.fn().mockResolvedValue({ operation: 'update' });
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'organization.person',
      listViewKey: 'organization.person.list',
      dataAdapter: {
        list: vi.fn(),
        get: vi.fn(),
        getRowId: () => 'person-1',
        update,
      },
    };
    const execution = await renderExecution(registration, vi.fn(), vi.fn());

    await act(async () => {
      await execution().execute({
        mode: 'edit',
        id: 'person-1',
        fields: [{
          key: 'positionId',
          labelKey: '岗位',
          modelField: 'PositionId',
          kind: 'lookup',
          emptyAsNull: true,
          lookup: { providerKey: 'organization.positions' },
        }],
        values: { positionId: undefined },
        detailFields: { PositionId: 'position-disabled' },
      });
    });

    expect(update).toHaveBeenCalledWith('person-1', { PositionId: null }, { PositionId: 'position-disabled' });
  });

  it('suppresses a second synchronous submit while the first mutation is still pending', async () => {
    let resolveCreate: (() => void) | undefined;
    const create = vi.fn(() => new Promise<StandardCrudOperationResult>((resolve) => {
      resolveCreate = () => resolve({ operation: 'create', payload: { receipt: 'opaque' } });
    }));
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'sample.record',
      listViewKey: 'sample.record.list',
      dataAdapter: {
        list: vi.fn(),
        get: vi.fn(),
        getRowId: () => 'record-1',
        create,
      },
    };
    const execution = await renderExecution(registration, vi.fn(), vi.fn());

    let firstSucceeded = false;
    let secondSucceeded = true;
    await act(async () => {
      const running = execution().execute({
        mode: 'create',
        fields: [...mutationFields],
        values: { name: ' Alice ' },
        detailFields: {},
      });
      secondSucceeded = await execution().execute({
        mode: 'create',
        fields: [...mutationFields],
        values: { name: ' Alice ' },
        detailFields: {},
      });
      resolveCreate?.();
      firstSucceeded = await running;
    });

    expect(create).toHaveBeenCalledTimes(1);
    expect(firstSucceeded).toBe(true);
    expect(secondSucceeded).toBe(false);
    expect(execution().pending).toBe(false);
  });
});
