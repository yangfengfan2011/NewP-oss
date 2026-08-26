import type {
  StandardCrudResourceRegistration,
// @ts-expect-error 列表结构不再通过公共扩展上下文暴露给模块。
  StandardCrudListExtensionContext,
} from '../src/index';

const registration: StandardCrudResourceRegistration = {
  resourceKey: 'sample.record',
  listViewKey: 'sample.record.list',
  dataAdapter: {
    list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
    get: async () => ({
      normalized: { fields: {}, summaries: {} },
      value: { fields: {}, summaries: {} },
    }),
    getRowId: () => 'record-1',
  },
};

const retiredFilterRenderer: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 筛选结构只由 List View 声明。
  renderFilters: () => null,
};

const retiredHeaderActionsRenderer: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 页级操作只由 List View pageActions 声明。
  renderHeaderActions: () => null,
};

const retiredRowActionsRenderer: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 行级操作只由 List View rowActions 声明。
  renderRowActions: () => null,
};

const retiredListActions: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 页级和行级操作结构只由 List View 声明。
  listActions: [],
};

void retiredFilterRenderer;
void retiredHeaderActionsRenderer;
void retiredRowActionsRenderer;
void retiredListActions;
void (undefined as unknown as StandardCrudListExtensionContext<unknown, unknown>);
