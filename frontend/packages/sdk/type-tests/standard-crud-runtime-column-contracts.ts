import type {
  StandardCrudColumnSet,
  StandardCrudListColumnProvider,
  StandardCrudListReadContext,
  StandardCrudResourceRegistration,
  StandardCrudRuntimeColumn,
} from '../src/index';

type WorkloadFilter = { keyword?: string };
type WorkloadRow = { id: string; memberName: string; currentWeekDay1: number };

const columns: StandardCrudRuntimeColumn[] = [{
  key: 'current-week.day-1',
  field: 'currentWeekDay1',
  title: '8月17日',
  valueKind: 'number',
  sortable: true,
  hideable: true,
}];

const provider: StandardCrudListColumnProvider<WorkloadFilter> = {
  load: async ({ filters, signal }): Promise<StandardCrudColumnSet> => {
    void filters.keyword;
    void signal.aborted;
    return { columns, contextToken: 'opaque-token' };
  },
};

const registration: StandardCrudResourceRegistration<WorkloadFilter, WorkloadRow> = {
  resourceKey: 'example.runtime-workload',
  listViewKey: 'example.runtime-workload.list',
  columnProvider: provider,
  dataAdapter: {
    list: async (_query, readContext?: StandardCrudListReadContext) => ({
      items: readContext?.columnContextToken
        ? [{ id: 'member-1', memberName: '示例成员', currentWeekDay1: 8 }]
        : [],
      total: 1,
      page: 1,
      pageSize: 20,
    }),
    get: async () => ({
      normalized: { fields: {}, summaries: {} },
      value: { fields: {}, summaries: {} },
    }),
    getRowId: (row) => row.id,
  },
};

void registration;
