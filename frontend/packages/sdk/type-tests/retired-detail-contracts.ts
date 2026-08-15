import type {
  StandardCrudFormMode,
  StandardCrudResourceRegistration,
// @ts-expect-error 整体详情 renderer 上下文已从公共 SDK 退役。
  StandardCrudDetailExtensionContext,
} from '../src/index';

// @ts-expect-error 详情呈现不再是表单 mode。
const retiredViewMode: StandardCrudFormMode = 'view';

const registration: StandardCrudResourceRegistration = {
  resourceKey: 'sample.record',
  apiRoute: '/api/sample/records',
  listViewKey: 'sample.record.list',
  detailViewKey: 'sample.record.detail',
  createEmptyFilters: () => ({}),
  buildListQuery: () => ({}),
  mapListItem: () => ({}),
  getRowId: () => 'record-1',
};

const retiredDetailActions: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 详情动作只由 Detail View pageActions 声明。
  detailActions: [],
};

const retiredDetailRenderer: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 普通详情结构只能由 Resolved Detail Schema 呈现。
  renderDetail: () => null,
};

const retiredHeaderRenderer: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 详情页头动作只由 Detail View pageActions 声明。
  renderDetailHeaderActions: () => null,
};

void retiredViewMode;
void retiredDetailActions;
void retiredDetailRenderer;
void retiredHeaderRenderer;
void (undefined as unknown as StandardCrudDetailExtensionContext<unknown>);
